# 11. Consume integration events idempotently

**Status:** Accepted

## Context

The Fraud Worker consumes the Authorization's decisions from the topic `authorizations` (ADR 0003) to keep a purchase
history per card, which the fraud rules will read. Delivery is at least once (ADR 0010): the outbox publisher may send
an event again after a crash or a timed-out send, converging concurrent writers may write the same event twice with
the same event id, and a Kafka consumer itself receives a message again whenever its offset was not committed (a crash,
a rebalance). Recording a purchase twice would distort the rules (two purchases count towards velocity), so each event
must take effect once. Later consumers (the Authorization will consume `fraud-signals`) need the same guarantees, so
the mechanism belongs in `CardPlatform.Messaging`, next to the outbox.

## Decision

**A reusable consumer in `CardPlatform.Messaging`.** A service registers it with
`AddIntegrationEventConsumer<TDbContext>(groupId, topics)` and one handler per event type with
`AddIntegrationEventHandler<TEvent, THandler>()` (`IIntegrationEventHandler<TEvent>`). Its context applies
`ApplyProcessedMessages()`, which maps the table `processed_messages`. Two parts:

- `IntegrationEventConsumer<TDbContext>`, a background service, runs the Kafka loop: consume, process, commit the offset.
- `IntegrationEventProcessor<TDbContext>` processes one message, independently of Kafka: reads the envelope with
  `EventSerializer` and, in a new DI scope and **one database transaction** of the context, inserts the event id into
  `processed_messages` with `INSERT ... ON CONFLICT DO NOTHING`; if a row was inserted it runs the handler, saves and
  commits; if not, the event was already processed and nothing else happens. The handler saves through the same scoped
  context (e.g. the Fraud Worker's `IUnitOfWork`), so **the mark and the effect are committed together, or neither
  is**. The context's retrying execution strategy (the Aspire default) wraps the whole transaction; a retry after a
  commit whose acknowledgement was lost finds the row and does nothing.

**`processed_messages`: `event_id` (uuid, primary key), `type`, `processed_at`.** Keyed by event id alone, because a
service database holds the processed events of one consumer group, and event ids are unique across types and topics.
A second consumer group sharing a database would need the group in the key; none does. Marking first, before the
handler runs, also covers two consumers processing the same message at once (the old and the new owner of a partition
during a rebalance): the second insert waits on the first transaction's row lock and then finds the row. Rows are never
deleted yet; cleaning up old ones is a later change.

**Offsets are committed by the consumer, after the database commit.** Automatic commits are off
(`EnableAutoCommit = false`), and each message's offset is committed synchronously (`Commit`) once its transaction
committed, also for duplicates and skipped messages, so the offset always advances past what was handled. A crash
between the two commits redelivers the message, which the processed mark turns into a no-op; a failed offset commit
(e.g. the partition was revoked) is only logged, for the same reason. Committing each message costs a round trip to the
broker, accepted for the volumes of this platform; storing offsets and committing them periodically is the next step if
it is not.

**Consumer group `fraud-worker`, from the earliest offset.** The group id and the other client settings come from the
Aspire Kafka integration (`AddKafkaConsumer<string, byte[]>`, connection string `kafka`, with its telemetry), under
`Aspire:Confluent:Kafka:Consumer:Config`; the service gives the default group id. A new group starts from the earliest
offset still retained, so a Fraud Worker deployed after the Authorization builds the history from every event Kafka
still has instead of only from the next one. The topics come from the service, and `Messaging:Consumer:Topics` can
replace them. One consumer per host.

**Unknown event types are logged and ignored.** An envelope whose type has no handler is skipped with a log (a warning
when the type is not in `EventTypes`, i.e. probably from a newer producer; debug when it is known but not handled by
this service), its offset is committed, and it is **not** marked as processed: nothing was done with it, so a later
version that handles the type can still process it from a replay. This keeps consumers forward compatible: a producer
can add an event type before every consumer knows it.

**Failures are retried, never skipped, for now.** A message that cannot be processed (not an envelope, a payload that
does not match its type, a handler that throws, the database down) is not committed: the consumer logs it, rewinds its
partition to it (`Seek`) and retries it after a bounded exponential backoff (`Messaging:Consumer:RetryInitialDelay`,
1 s, doubled up to `RetryMaxDelay`, 30 s), so nothing is lost and the events of a card keep their order. A handler
signals data it can never accept with `IntegrationEventRejectedException` (the Fraud Worker does so when its use case
returns a failure, e.g. an invalid currency or capturing a voided purchase); it is retried like the rest today. Bounded
retries and a dead-letter topic will plug into this loop and route these, and the messages that keep failing, there.
A fatal Kafka error stops the application so the orchestrator restarts it, as for the producer.

**Health check off by default.** As for the producer (ADR 0010), the integration's Kafka health check is disabled
unless `Aspire:Confluent:Kafka:Consumer:DisableHealthChecks` is set: a Kafka outage only delays processing, as the
events wait in Kafka, and the consumer's logs show it.

**Topics.** In Development the Fraud Worker also creates `authorizations` on startup (`AddKafkaTopicInitializer`), so
it does not depend on starting after the Authorization; an existing topic is left as it is.

**The Fraud Worker's side (ADR 0004).** The Domain has `CardPurchase` (keyed by authorization id: amount and currency,
status approved, declined with its reason, captured or voided, when it was authorized and completed) and `CardHistory`
(a card's purchases, with the approved ones in a time window or a currency, for the rules). The Application has one
command and handler per event type, which are idempotent on their own too: a purchase already recorded is left as it
is, and a capture or void of a purchase whose approval is not in the history records it from the event, which carries
the whole purchase. Captures and voids are recorded, not ignored: the history then says what happened to each
purchase, and an approved purchase still counts as approved for the rules after it is captured or voided. The adapters
that map each contract to its command are `IIntegrationEventHandler<TEvent>` implementations in Infrastructure, the
only layer that may use `CardPlatform.Messaging`; the Worker remains the composition root and hosts them through
`AddFraudInfrastructure()`.

## Consequences

- Every event takes effect once, however many times it is delivered; the at-least-once delivery of ADR 0010 is
  absorbed by one insert per message.
- A consuming service needs PostgreSQL and its own `processed_messages` table, written in its own database.
- Throughput is one message, one transaction and one offset commit at a time per consumer; a topic's partitions (3)
  bound how many consumers of a group can share the work.
- A message that can never be processed blocks its partition, retried with backoff and logged as an error, until the
  dead-letter topic exists; other partitions keep flowing, slowed by the backoff of the shared loop.
- `processed_messages` grows with every event; cleaning up old rows (older than the topic's retention) is not done yet.
