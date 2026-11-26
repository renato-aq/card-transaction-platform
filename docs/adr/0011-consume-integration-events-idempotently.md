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
A fatal error of the Kafka consumer stops the application so the orchestrator restarts it. A fatal error of the
producer while dead-lettering does not: the producer is replaced and the send retried, as in the outbox (ADR 0010).

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

## Amendment (2026-11-25)

The Authorization became the second consumer (spec 006 of the fraud task); the original text above is kept as it was
written.

- **The Authorization consumes `fraud-signals`** in its own consumer group, `authorization-api`, with its own
  `processed_messages` table in `authorizationdb`. Its inbound adapter, `FraudSuspectedHandler` (an
  `IIntegrationEventHandler<FraudSuspected>` in Infrastructure), sends the existing `BlockCard` command, which saves
  through the scoped `AuthorizationDbContext`, so the block and the processed mark are committed in the consumer's
  transaction. A suspicion of a card the Authorization does not know is logged as a warning and confirmed (marked, its
  offset committed): there is nothing to block, and retrying it could never succeed. The suspicion's rule and reason
  are only logged; the card's contract does not change.
- **`Messaging:Consumer:Enabled`** (default `true`) turns the consumer off, as `Messaging:Outbox:Enabled` does the
  publisher: `AddIntegrationEventConsumer` then registers nothing (no Kafka consumer, no background service) and the
  events wait in Kafka. The Authorization's integration tests that run without a broker set both switches to `false`.

## Amendment (2026-11-25)

Retries became bounded and a dead-letter topic was added (spec 007 of the fraud task), replacing "failures are retried,
never skipped, for now"; the original text above is kept as it was written. It applies to every consumer built on
`AddIntegrationEventConsumer` (the Fraud Worker on `authorizations`, the Authorization on `fraud-signals`).

- **Failures are classified.** *Permanent*: the message is not a valid envelope or its payload does not match its type
  (`JsonException`, including a null value), or the handler threw `IntegrationEventRejectedException` (its use case
  rejected the data). Retrying cannot change the outcome, so it goes to the dead-letter topic after the first attempt.
  *Temporary*: any other exception (the database down, a timeout, a handler bug). It is retried in process: the
  consumer rewinds the partition to the message (`Seek`) and processes it again after an exponential backoff
  (`RetryInitialDelay` 1 s, doubled up to `RetryMaxDelay` 30 s), before any later message of that partition, up to
  `Messaging:Consumer:MaxAttempts` attempts (default 5, the first included), and then dead-letters it. An unknown event
  type is still skipped, not dead-lettered: it is forward compatibility, not an error.
- **Attempts are counted per message**, keyed by topic, partition and offset (`FailedMessageTracker`), no longer by a
  loop-wide counter that any success reset: messages of other partitions processed between two retries no longer give
  a failing message a fresh budget. A different offset at the head of the partition starts from zero. The counter
  lives in memory: a restart or a rebalance gives the message a new budget, which errs on the side of retrying. The
  backoff still pauses the whole loop (one consumer thread), so other partitions are slowed while a message waits for
  its retry, as before.
- **Dead-lettering.** The message goes to `Topics.DeadLetterOf(topic)` (`<topic>.dlt`, e.g. `authorizations.dlt`,
  `fraud-signals.dlt`) through `KafkaDeadLetterProducer`, with the **original key and value byte for byte** (a malformed
  value is kept as it arrived), the original headers (`event-id`, `event-type`, anything else), and error headers
  (`DeadLetterHeaders`, UTF-8 text): `dlt-reason` (the exception's message, at most 1000 characters; for a temporary
  failure, its root cause's, `GetBaseException()`, so a wrapper such as EF's `RetryLimitExceededException` does not hide
  the Npgsql error), `dlt-exception-type` (of the same exception), `dlt-failure-kind` (`permanent` or
  `retries-exhausted`), `dlt-attempts`, `dlt-original-topic`, `dlt-original-partition`, `dlt-original-offset`,
  `dlt-consumer-group` (the effective group, after configuration) and
  `dlt-failed-at` (UTC, ISO 8601). Error headers already on a message replayed from a dead-letter topic are replaced, not
  repeated. The send awaits the broker's acknowledgement (idempotent producer, `acks=all`); **only then is the original
  offset committed** and the partition moves on. Every dead-lettered message is logged as an error (`ConsumerLog` event
  120) with its topic, partition, offset, event id and type when known (from the headers, or from the envelope if it
  parses), dead-letter topic, attempts, failure kind and reason (the same message as `dlt-reason`).
- **If the dead-letter send fails** (broker down, topic missing with auto-creation off), nothing is committed: the
  consumer logs it (event 121), rewinds to the message and tries the send again after a backoff, without processing the
  message again, for as long as it takes. No message is lost; the partition waits, as it did before this change. If the
  offset commit after a successful send fails (a rebalance), the message is processed again by its next owner and may
  be dead-lettered twice: the dead-letter topic is at least once too.
- **A dead-lettered message is not marked as processed.** It is not written to `processed_messages` (the processor's
  transaction rolled back, or never started), so replaying it later, once the cause is fixed, processes it normally;
  dedup by event id still makes a replay of an event that did get processed harmless. Reprocessing is manual (re-produce
  the dead-letter message's key and value to the original topic, e.g. from Kafka UI or a CLI, see the README); there is
  no automatic reprocessing.
- **One producer per service.** `AddIntegrationEventConsumer` registers the Aspire Kafka producer (`string`, `byte[]`,
  `KafkaProducerDefaults`) unless `AddOutboxPublisher` already did, and vice versa, so a consumer can dead-letter even
  with the outbox publisher disabled, and a service that does both shares one producer.
- **Topics.** In Development each service also creates the dead-letter topic of the topic it consumes
  (`authorizations.dlt` in the Fraud Worker, `fraud-signals.dlt` in the Authorization), with the same partitions as the
  others. The Fraud Worker no longer creates `authorizations` when its consumer is off, nor anything when both its
  consumer and its publisher are off.

**The trade-off of a bounded budget for infrastructure outages.** The spec makes "database unavailable" a temporary
failure with a configurable limit, so a database outage longer than the budget dead-letters the message at the head of
each partition. Treating connection failures as "retry forever" was considered and not chosen: it contradicts the
spec's configurable limit, and telling an outage from a bug by exception type is fragile (an `NpgsqlException` can be
either). The impact is bounded instead: each attempt already runs through the context's retrying execution strategy
(Aspire's Npgsql default retries transient errors for about a minute before giving up), so five attempts plus the
backoff between them cover several minutes of outage; only the message at the head of a partition is dead-lettered per
budget, not the whole backlog, since the next one starts its own budget; and the dead-lettered message keeps its key,
value and original position in its headers, so it can be replayed. A deployment that expects longer outages raises
`MaxAttempts` or `RetryMaxDelay`. The cost of a dead-lettered message is ordering: a later event of the same card may be
processed before the replay. The Fraud Worker's history tolerates it (a capture or void carries the whole purchase, and
recording is idempotent), and blocking a card is idempotent.

The consequence "a message that can never be processed blocks its partition until the dead-letter topic exists" no
longer holds: it blocks it for at most its retry budget.
