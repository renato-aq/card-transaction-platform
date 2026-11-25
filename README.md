# Card Transaction Platform

[![CI](https://github.com/renato-aq/card-transaction-platform/actions/workflows/ci.yml/badge.svg)](https://github.com/renato-aq/card-transaction-platform/actions/workflows/ci.yml)

An event-driven card transaction platform built with **.NET 10** and **.NET Aspire**: real-time authorization,
a double-entry ledger, asynchronous fraud detection and a high-volume daily settlement batch.

> **Status:** work in progress. See the [roadmap](#roadmap).

## Architecture

```mermaid
flowchart LR
    client([Client / POS]) -->|POST /authorizations| auth[Authorization API]
    auth -->|balance & limits| ledger[Ledger API]
    ledger --> db[(PostgreSQL)]
    ledger -->|outbox| kafka{{Kafka}}
    kafka --> fraud[Fraud Worker]
    db --> settlement[Settlement Job]
    settlement --> file[/Daily settlement file/]
```

| Service | Responsibility |
|---|---|
| **Authorization API** | Approves or declines card purchases with low latency; idempotent requests |
| **Ledger API** | Double-entry bookkeeping, balances, transactional outbox for events |
| **Fraud Worker** | Consumes transaction events and flags suspicious patterns |
| **Settlement Job** | Generates the daily settlement file in batches from the ledger |

Shared projects: `ServiceDefaults` (OpenTelemetry, health checks, resilience, service discovery) and
`Contracts` (integration events).

## Tech stack

- **.NET 10**, ASP.NET Core, Entity Framework Core
- **.NET Aspire** for local orchestration and observability dashboard
- **PostgreSQL**, **Kafka**
- **OpenTelemetry** (traces, metrics, logs)
- **xUnit**, WebApplicationFactory, Testcontainers
- **GitHub Actions** CI

## Getting started

Prerequisites:

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A container runtime: [Docker Desktop](https://www.docker.com/products/docker-desktop/) or [OrbStack](https://orbstack.dev/)

Run the whole platform (PostgreSQL, Kafka and all services) with one command:

```bash
dotnet run --project src/CardPlatform.AppHost
```

Or, with the [Aspire CLI](https://aspire.dev) installed, from the repository root:

```bash
aspire run
```

The Aspire dashboard URL is printed in the console. Kafka UI is available from the dashboard.

Run the tests (the Ledger and Authorization API integration tests use [Testcontainers](https://testcontainers.com/) to start a
real PostgreSQL, so the container runtime must be running):

```bash
dotnet test
```

## Ledger

The Ledger is the source of truth for balances. It follows a **hexagonal architecture** with **pragmatic CQRS**
([ADR 0004](docs/adr/0004-use-hexagonal-architecture-with-pragmatic-cqrs.md)), split into projects so the compiler
enforces the dependency rule (checked by architecture tests):

| Project | What lives there | Depends on |
|---|---|---|
| `Ledger.Domain` | `Account`, `LedgerTransaction`, `Entry`, `Money` and the invariants | nothing |
| `Ledger.Application` | Command/query handlers, ports (repositories, unit of work), retry decorator | Domain |
| `Ledger.Infrastructure` | EF Core + PostgreSQL adapters, migrations, query implementations | Application |
| `Ledger.Api` | Minimal API endpoints, `Result` → `ProblemDetails`, composition root | Application, Infrastructure |

- **Commands** (open account, post transaction) go through the domain model, which enforces the rules;
  **queries** (balance) read the database directly into DTOs. One database, no mediator library.
- **Double-entry** ([ADR 0005](docs/adr/0005-use-double-entry-ledger-model.md)): every transaction has debits equal to
  credits and every account uses `balance = credits − debits`, so the sum of all balances is always zero.
  A top-up debits `settlement` and credits `card`; a purchase debits `card` and credits `merchant`.
  A card balance can never go negative.
- **Optimistic concurrency** ([ADR 0006](docs/adr/0006-use-optimistic-concurrency-for-ledger-accounts.md)): the
  PostgreSQL `xmin` column is the concurrency token of each account. On a conflict, posting a transaction is
  retried up to 3 times with jittered backoff; if it still conflicts the API answers `409` with
  `code: concurrency_conflict`.

### Ledger API

With the platform running, the Ledger API listens on `http://localhost:5211` (the `http` endpoint of `ledger-api`
in the Aspire dashboard, also shown by `aspire describe ledger-api`). In Development, the OpenAPI document is at
`/openapi/v1.json`. The examples below use `curl` and [jq](https://jqlang.org/) to capture the account ids:

```bash
LEDGER=http://localhost:5211
```

Open a settlement, a card and a merchant account:

```bash
SETTLEMENT=$(curl -s -X POST "$LEDGER/accounts" -H 'Content-Type: application/json' \
  -d '{"type":"settlement","currency":"BRL"}' | jq -r .id)
CARD=$(curl -s -X POST "$LEDGER/accounts" -H 'Content-Type: application/json' \
  -d '{"type":"card","currency":"BRL"}' | jq -r .id)
MERCHANT=$(curl -s -X POST "$LEDGER/accounts" -H 'Content-Type: application/json' \
  -d '{"type":"merchant","currency":"BRL"}' | jq -r .id)
```

Each call answers `201 Created` with `Location: /accounts/{id}` and the new account:

```json
{"id":"<id>","type":"card","currency":"BRL","balance":{"amount":0.00,"currency":"BRL"}}
```

Top up the card with 100 (debit settlement, credit card):

```bash
curl -s -X POST "$LEDGER/transactions" -H 'Content-Type: application/json' -d '{
  "description": "Top-up",
  "currency": "BRL",
  "entries": [
    { "accountId": "'"$SETTLEMENT"'", "direction": "debit", "amount": 100.00 },
    { "accountId": "'"$CARD"'", "direction": "credit", "amount": 100.00 }
  ]
}'
```

Buy something for 30 (debit card, credit merchant):

```bash
curl -s -X POST "$LEDGER/transactions" -H 'Content-Type: application/json' -d '{
  "description": "Coffee shop",
  "currency": "BRL",
  "entries": [
    { "accountId": "'"$CARD"'", "direction": "debit", "amount": 30.00 },
    { "accountId": "'"$MERCHANT"'", "direction": "credit", "amount": 30.00 }
  ]
}'
```

Both answer `201 Created` with the posted transaction:

```json
{"id":"<id>","description":"Coffee shop","currency":"BRL","createdAt":"<timestamp>","entries":[{"accountId":"<card>","direction":"debit","amount":30.00},{"accountId":"<merchant>","direction":"credit","amount":30.00}]}
```

Get the card balance:

```bash
curl -s "$LEDGER/accounts/$CARD/balance"
```

```json
{"accountId":"<card>","balance":{"amount":70.00,"currency":"BRL"}}
```

Errors are `application/problem+json` with a machine-readable `code`: `400` for invalid input (e.g.
`invalid_amount`, `invalid_currency`), `404` `account_not_found`, `422` for business rules
(`unbalanced_transaction`, `currency_mismatch`, `insufficient_funds`) and `409` `concurrency_conflict`.
For example, spending more than the card holds:

```bash
curl -s -X POST "$LEDGER/transactions" -H 'Content-Type: application/json' -d '{
  "currency": "BRL",
  "entries": [
    { "accountId": "'"$CARD"'", "direction": "debit", "amount": 500.00 },
    { "accountId": "'"$MERCHANT"'", "direction": "credit", "amount": 500.00 }
  ]
}'
```

`422 Unprocessable Entity`, `Content-Type: application/problem+json`:

```json
{
  "type": "https://tools.ietf.org/html/rfc4918#section-11.2",
  "title": "Business rule violated",
  "status": 422,
  "detail": "Account <card> has a balance of 70.00 BRL and cannot be debited 500.00 BRL.",
  "code": "insufficient_funds",
  "traceId": "<trace id>"
}
```

## Authorization → Ledger

The Authorization API talks to the Ledger through the `ILedgerClient` port (Application), implemented by a typed
`HttpClient` in Authorization.Infrastructure. The base address is `https+http://ledger-api`, resolved by Aspire service
discovery. The client uses the standard resilience handler that `ServiceDefaults` adds to every `HttpClient`, with
settings tuned for authorization calls (section `Ledger` in configuration, see `LedgerClientOptions`):

| Setting | Value | Why |
|---|---|---|
| Total timeout | 2 s | a card authorization cannot wait long; the whole call, retries included, fits in it |
| Attempt timeout | 1 s | a slow attempt is abandoned while there is still budget for a retry |
| Retries | 2, exponential backoff from 100 ms with jitter | only for calls that are safe to repeat (see below) |
| Circuit breaker | standard defaults (opens at ≥ 10% failures over ≥ 100 attempts in 30 s, for 5 s) | stops hammering a Ledger that is down |

Only safe calls are retried: reads (`GET` balance and hold) and placing a hold, which the Ledger makes idempotent by
the hold id (a retry returns the same hold instead of creating a second one). Opening an account, capturing and voiding
are never retried: repeating them would open another account or fail with `hold_not_pending`.

**When the Ledger is down or slow** (timeout, open circuit, connection error or a `5xx` after the retries), the
client returns the error `ledger_unavailable`, which the Authorization API answers with `503 Service Unavailable`
(`application/problem+json`, `code: ledger_unavailable`) within about 2 seconds, instead of hanging. A capture or void
that ends in `ledger_unavailable` has an unknown outcome and is reconciled later by reading the hold
(`GET /holds/{id}`). Errors returned by the Ledger keep their code (e.g. `insufficient_funds`, `hold_not_pending`)
and are classified by status: `400` validation, `404` not found, `409` conflict, `422` business rule.

## Project structure

```
src/
  CardPlatform.AppHost/               Aspire orchestration
  CardPlatform.ServiceDefaults/       Shared observability, health checks, resilience
  CardPlatform.Contracts/             Integration events
  CardPlatform.Authorization.Domain/         Authorization entities and invariants
  CardPlatform.Authorization.Application/    Authorization use cases and ports
  CardPlatform.Authorization.Infrastructure/ EF Core, PostgreSQL (authorizationdb), migrations, Ledger HTTP client
  CardPlatform.Authorization.Api/            Authorization HTTP endpoints
  CardPlatform.Ledger.Domain/         Ledger entities, value objects and invariants
  CardPlatform.Ledger.Application/    Ledger use cases (command/query handlers) and ports
  CardPlatform.Ledger.Infrastructure/ EF Core, PostgreSQL, migrations
  CardPlatform.Ledger.Api/            Ledger HTTP endpoints
  CardPlatform.Fraud.Worker/
  CardPlatform.Settlement.Job/
tests/
  CardPlatform.Architecture.Tests/    Dependency rule between the Ledger and Authorization layers
  CardPlatform.Ledger.Domain.Tests/
  CardPlatform.Ledger.Application.Tests/
  CardPlatform.Ledger.Api.Tests/      Integration tests (WebApplicationFactory + Testcontainers PostgreSQL)
  CardPlatform.Authorization.Domain.Tests/
  CardPlatform.Authorization.Application.Tests/
  CardPlatform.Authorization.Infrastructure.Tests/ Ledger client against a fake Ledger (retries, timeouts, circuit)
  CardPlatform.Authorization.Api.Tests/ Integration tests (WebApplicationFactory + Testcontainers PostgreSQL)
docs/adr/                             Architecture Decision Records
```

## Roadmap

- [x] Solution skeleton, Aspire orchestration, CI
- [x] Ledger: double-entry accounts and transactions (EF Core + PostgreSQL)
- [ ] Authorization API: idempotent authorization flow
- [ ] Transactional outbox + Kafka integration events
- [ ] Fraud Worker: rule-based detection
- [ ] Settlement Job: batched file generation with before/after benchmarks
- [ ] Integration tests with Testcontainers
- [ ] Load tests with k6 and published results
- [ ] Kubernetes manifests

## Architecture decisions

See [docs/adr](docs/adr) for the reasoning behind the main technical choices:

1. [Record architecture decisions](docs/adr/0001-record-architecture-decisions.md)
2. [Use .NET Aspire for local orchestration](docs/adr/0002-use-dotnet-aspire-for-local-orchestration.md)
3. [Use Kafka for integration events and PostgreSQL for the ledger](docs/adr/0003-use-kafka-and-postgresql.md)
4. [Use hexagonal architecture with pragmatic CQRS](docs/adr/0004-use-hexagonal-architecture-with-pragmatic-cqrs.md)
5. [Use a double-entry ledger model](docs/adr/0005-use-double-entry-ledger-model.md)
6. [Use optimistic concurrency for ledger accounts](docs/adr/0006-use-optimistic-concurrency-for-ledger-accounts.md)
