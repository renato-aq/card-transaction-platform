# 3. Use Kafka for integration events and PostgreSQL for the ledger

**Status:** Accepted

## Context

Transaction events must be consumed by several independent services (fraud detection now, others
later) and be replayable. The ledger requires strong consistency and ACID transactions.

## Decision

- **Kafka** for integration events: durable, ordered per partition (partitioned by card id), and
  replayable by new consumers.
- **PostgreSQL** for the ledger: ACID transactions, mature EF Core provider, and runs easily in a
  container.

## Consequences

- Events and database writes must be kept consistent; this will be handled with a transactional
  outbox (future ADR).
- Consumers must be idempotent, since Kafka delivers at least once.
