# 4. Use hexagonal architecture with pragmatic CQRS

**Status:** Accepted

## Context

The Ledger and Authorization services hold the core business rules of the platform: debits must equal
credits, money must be handled precisely, and concurrent requests must never produce an inconsistent
balance. These rules should be testable without a database or a web server, and must not depend on
EF Core, ASP.NET Core or Kafka.

Writes and reads also have different needs. Writes enforce invariants through the domain model; reads
(balances, statements and the high-volume settlement batch) only need fast projections of the data.

## Decision

**Hexagonal architecture (ports and adapters)** in the services with business rules (Ledger and
Authorization), split into separate projects so the compiler enforces the dependency rule:

| Project | Role | Depends on |
|---|---|---|
| `*.Domain` | Entities, value objects and invariants | nothing |
| `*.Application` | Command and query handlers (inbound ports) and the interfaces of outbound ports (repositories, unit of work, clock) | Domain |
| `*.Infrastructure` | Outbound adapters: EF Core, PostgreSQL, migrations, outbox, Kafka | Application |
| `*.Api` | Inbound adapter: HTTP endpoints and composition root | Application, Infrastructure |

Persistence concerns stay in Infrastructure; for example, optimistic concurrency uses PostgreSQL `xmin`
as an EF Core shadow property, so the domain model is unaware of it. Architecture tests verify the
dependency rule.

**Pragmatic CQRS** inside each of those services:

- **Commands** go through the domain model and repositories, which enforce the invariants.
- **Queries** bypass the domain model and read directly from the database (no-tracking projections or
  plain SQL), returning DTOs.
- A **single database** is shared by both sides. No separate read store and no event sourcing.

**Dispatching** uses small in-house interfaces (`ICommandHandler<TCommand, TResult>`,
`IQueryHandler<TQuery, TResult>`) registered in dependency injection. Cross-cutting behaviour such as
logging or validation is added with decorators. No mediator library is used.

**Expected business failures** (unbalanced transaction, unknown account, insufficient funds) are returned
as a `Result` and translated to `ProblemDetails` responses by the API. Exceptions are reserved for
unexpected failures.

The **Fraud Worker** and **Settlement Job** do not follow this structure: they have little domain logic,
and the settlement batch is a read-only, performance-critical workload where extra layers would get in
the way of streaming and batching optimizations.

## Consequences

- Domain rules are covered by fast unit tests with no infrastructure.
- Infrastructure (database, message broker) can change without touching the domain or use cases.
- Queries can be optimized independently from the write model; a dedicated read model can be
  introduced later without changing the command side.
- More projects and more mapping code than a single-project service; this cost is accepted only where
  business complexity justifies it.
- Explicit handler interfaces mean no third-party mediator dependency or licensing concerns, at the cost
  of writing a small amount of plumbing code.
