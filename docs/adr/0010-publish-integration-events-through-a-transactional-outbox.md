# 10. Publish integration events through a transactional outbox

**Status:** Accepted

## Context

The Authorization's decisions (approved, declined, captured, voided) must reach Kafka, keyed by card id, so the
Fraud Worker and later consumers can react to them (ADR 0003). A decision is saved in PostgreSQL and the event is
sent to Kafka: two systems that cannot share a transaction. Every direct approach loses or invents events:

- **Save, then publish:** a crash or a Kafka outage after the commit loses the event, although the decision exists.
- **Publish, then save:** a failed save leaves an event for a decision that was never recorded.
- **Publish inside the request and fail it when Kafka is down:** makes Kafka part of the synchronous authorization
  path, which must answer within a predictable time (ADR 0009) and has nothing to gain from it.

ADR 0003 deferred this to a transactional outbox. Its first half is already in place: every decision writes its
event to `outbox_messages` in the same `SaveChanges`, in the same transaction, as the authorization (`IOutbox` in
the Application, implemented with EF Core in Infrastructure). Concurrent writers that converge on the same final
state (ADR 0007, amendment) may each write the event, with the same deterministic event id. This decision covers how
those rows get to Kafka.

## Decision

**A background publisher reads the outbox and publishes it.** `OutboxPublisher<TDbContext>`, a hosted service in
`CardPlatform.Messaging` registered with `AddOutboxPublisher<TDbContext>()`, runs publishing cycles. Each cycle
(`OutboxBatchPublisher<TDbContext>`), in one database transaction:

1. takes a PostgreSQL advisory lock, `pg_try_advisory_xact_lock(<key>)`; if another instance holds it, the cycle
   does nothing and the publisher tries again after the polling interval;
2. reads up to 100 unpublished rows (`published_at IS NULL`) in `id` order;
3. sends them one at a time, each to its row's topic with key = partition key (the card id), and waits for the
   broker's acknowledgement before sending the next; every send counts one more `attempts`, and only an acknowledged
   row gets `published_at`;
4. stops at the first failed send, so no later row overtakes it, saves the marks and attempts and commits, which
   also releases the lock (PostgreSQL releases it too if the connection is lost).

A full successful batch is followed by another cycle at once; an empty or partial one by the polling interval; a
failed one by a bounded exponential backoff. Failures never stop the service: when Kafka or the database is back,
the next cycle publishes what waited.

| Setting (`Messaging:Outbox`) | Default |
|---|---|
| `BatchSize` | 100 |
| `PollingInterval` | 1 s |
| `RetryInitialDelay` / `RetryMaxDelay` | 1 s, doubled after each failed cycle, at most 30 s |
| `LockKey` | a constant, unique among the advisory locks of a service database |
| `Enabled` | `true`; `false` registers nothing Kafka-related and the events stay in the outbox |

**Kafka producer.** Through the Aspire integration (`Aspire.Confluent.Kafka`, `AddKafkaProducer<string, byte[]>`,
connection string `kafka`, with its telemetry), forced to be **idempotent with `acks=all`**: an acknowledged event
is on every in-sync replica, and the producer's own retries neither duplicate nor reorder events in a partition. The
delivery timeout (`message.timeout.ms`) defaults to 10 s instead of librdkafka's 5 minutes, so a cycle against an
unreachable broker fails quickly and backs off; it can be changed under `Aspire:Confluent:Kafka:Producer`. The
message value is the whole `EventEnvelope`, written by `EventSerializer`; the headers `event-id` and `event-type`
repeat its id and type so consumers can route or deduplicate without parsing it. The integration's Kafka health
check is disabled by default, so the service's readiness (`/health`) does not depend on Kafka: an outage only delays
events, and the outbox backlog and the publisher's logs show it.

**What is ordered, precisely.** Events of one card are published to one partition (key = card id) and Kafka keeps
their order there. The publisher sends the rows it reads in `id` order, waits for each acknowledgement and stops at
the first failure, so among the rows of a card visible to a cycle, a later id never reaches Kafka before an earlier
one. Ids come from one sequence and are assigned when the row is inserted, not when its transaction commits, so
neither the id nor `created_at` is commit order: a cycle can see row 7 of a card committed while row 6, inserted by
a transaction still open, is not visible yet; it publishes 7 and, in a later cycle, 6. That only happens when the
two transactions overlapped, that is, two requests for the same card were in flight at the same time (e.g. two
purchases), and those have no defined order anyway: which one comes first is decided by the race. **Causally related
events are always published in the order they happened:** the capture or void of an authorization reads its
committed approval, so its transaction starts after the approval committed, its row gets a larger id, and any
snapshot that sees it also sees the approval's row. Rows are ordered by `id` and not by `created_at` because
`created_at` comes from each instance's clock, which can disagree across instances; the partial index of unpublished
rows is on `id` for that reason.

Only publishing rows older than the oldest open transaction (`xmin` against
`pg_snapshot_xmin(pg_current_snapshot())`) was considered to close that gap, and rejected: an older transaction can
still insert a row with a larger id later, so it does not close the gap, and every event would wait behind any long
transaction of the database.

**One publisher at a time.** The advisory lock makes the instances of a service take turns: two instances never
publish the same rows in parallel. `SELECT ... FOR UPDATE SKIP LOCKED` was considered and rejected: it lets instance
A lock row 1 and instance B row 2 of the same card, and B can publish first, breaking the order above. The cost is
that publishing throughput is that of one instance at a time, which is ample here; locking per partition of card ids
would be the next step if it is not.

**At least once.** An event can be published more than once, never lost:

- the process or the database connection fails after the broker acknowledged an event and before the commit: the row
  is still unpublished and the next cycle sends it again;
- a send times out but did reach the broker (e.g. a stalled broker that recovers): it is sent again, and the copy
  may appear after later events of the card;
- converging concurrent writers wrote the same event twice, with the same event id (see Context).

Consumers must therefore be idempotent by event id (ADR 0003), which they can be because every copy carries the same
id; a consumer that keeps the first copy of each id sees the order described above.

**Topics.** In Development the Authorization creates the topics it uses on startup, before the publisher runs
(`KafkaTopicInitializer`, an AdminClient): `authorizations`, which it publishes, and `fraud-signals`, which it will
consume. Each gets **3 partitions** (a card always lands on the same one, and up to three consumers of a group can
share the work) and a replication factor of 1, the single local broker (`Messaging:Kafka:Topics`). Existing topics
are left as they are, and a failure is logged without stopping startup. Other environments provision topics outside
the application.

**Layers (ADR 0004).** The publisher and the Kafka producer live in `CardPlatform.Messaging`, used only by the
services' Infrastructure layers (`AddAuthorizationInfrastructure` registers them); the Application keeps writing
through its `IOutbox` port. Architecture tests check that Domain, Application and Api depend on neither Messaging
nor Kafka. The Fraud Worker will reuse the same publisher for `FraudSuspected`.

## Consequences

- A decision and its event are saved atomically; a Kafka outage delays events but neither fails authorizations nor
  loses events, which are published, in order per card, once the broker is back.
- Events reach Kafka after a delay of up to the polling interval (1 s by default) plus the send.
- Sends are sequential, one acknowledgement at a time, which bounds throughput per cycle; batching sends would need
  a more careful handling of partial failures to keep the order.
- Every consumer must deduplicate by event id; this is the price of at-least-once delivery and is required by ADR
  0003 anyway.
- A row that can never be published (e.g. larger than the broker accepts) stops publishing for the whole service and
  its `attempts` keeps growing; parking such rows in a dead-letter table is not done yet.
- A fatal error of the idempotent producer (e.g. an unrecoverable sequence or epoch error), after which it cannot be
  used, does not stop the application, which would take the synchronous purchase path down for a messaging error:
  `KafkaProducerProvider` disposes the producer and builds a new one with the same settings and telemetry (the Aspire
  integration's factory), and the cycle that failed backs off as after any failure. The row stays unpublished and is
  sent again first, so order and at-least-once delivery hold; a new producer only restarts the idempotence sequence,
  so the failed send may also have been stored, a duplicate consumers already drop.
- The outbox grows with every event: cleaning up published rows is not done yet and will need its own change.
- The publisher holds a database transaction open while it sends a batch, at most about the delivery timeout when
  the broker is unreachable.
