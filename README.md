# Card Transaction Platform

[![CI](https://github.com/renato-aq/card-transaction-platform/actions/workflows/ci.yml/badge.svg)](https://github.com/renato-aq/card-transaction-platform/actions/workflows/ci.yml)

An event-driven card transaction platform built with **.NET 10** and **.NET Aspire**: real-time authorization,
a double-entry ledger, asynchronous fraud detection and a high-volume daily settlement batch.

> **Status:** work in progress. See the [roadmap](#roadmap).

## Architecture

```mermaid
flowchart LR
    client([Client / POS]) -->|"1. authorize<br/>3. capture or void"| auth[Authorization API]
    auth --> authdb[(authorizationdb)]
    auth -->|"2. place hold<br/>4. capture or void hold"| ledger[Ledger API]
    ledger --> ledgerdb[(ledgerdb)]
    ledger -.->|outbox| kafka{{Kafka}}
    kafka -.-> fraud[Fraud Worker]
    ledgerdb -.-> settlement[Settlement Job]
    settlement -.-> file[/Daily settlement file/]
```

Solid arrows are implemented; dotted arrows are the next phases (see the [roadmap](#roadmap)). A purchase is
**authorized** first, which places a **hold** on the card balance in the Ledger, and **captured** (the money goes to
the merchant) or **voided** (the money goes back to the card) later; see [Authorization](#authorization).

| Service | Responsibility |
|---|---|
| **Authorization API** | Approves or declines card purchases with low latency, idempotent by `Idempotency-Key`; captures and voids them |
| **Ledger API** | Double-entry bookkeeping, balances and authorization holds; transactional outbox for events (planned) |
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
real PostgreSQL, so the container runtime must be running; the Authorization integration tests also run the real Ledger
API in process, with its own database):

```bash
dotnet test
```

## Ledger

The Ledger is the source of truth for balances. It follows a **hexagonal architecture** with **pragmatic CQRS**
([ADR 0004](docs/adr/0004-use-hexagonal-architecture-with-pragmatic-cqrs.md)), split into projects so the compiler
enforces the dependency rule (checked by architecture tests):

| Project | What lives there | Depends on |
|---|---|---|
| `Ledger.Domain` | `Account`, `LedgerTransaction`, `Entry`, `Hold`, `Money` and the invariants | nothing |
| `Ledger.Application` | Command/query handlers, ports (repositories, unit of work), retry decorator | Domain |
| `Ledger.Infrastructure` | EF Core + PostgreSQL adapters, migrations, query implementations | Application |
| `Ledger.Api` | Minimal API endpoints, `Result` → `ProblemDetails`, composition root | Application, Infrastructure |

- **Commands** (open account, post transaction, place/capture/void a hold) go through the domain model, which
  enforces the rules; **queries** (balance, hold) read the database directly into DTOs. One database, no mediator
  library.
- **Double-entry** ([ADR 0005](docs/adr/0005-use-double-entry-ledger-model.md)): every transaction has debits equal to
  credits and every account uses `balance = credits − debits`, so the sum of all balances is always zero.
  Account types are `settlement`, `card`, `merchant` and `holds`. A top-up debits `settlement` and credits `card`; a
  direct purchase debits `card` and credits `merchant`. A card balance can never go negative.
- **Holds** ([ADR 0007](docs/adr/0007-model-authorization-holds-in-the-ledger.md)): an authorized purchase is a hold,
  also modelled as double-entry. Placing it debits `card` and credits the `holds` account of the currency (one per
  currency, created on first use); capturing it debits `holds` and credits `merchant`; voiding it debits `holds` and credits
  `card` back. So the card balance is always the available balance, and a hold goes `pending` → `captured` or `voided`, both final
  (otherwise `hold_not_pending`). Holds are idempotent by id: placing the same hold again returns the existing one.
- **Optimistic concurrency** ([ADR 0006](docs/adr/0006-use-optimistic-concurrency-for-ledger-accounts.md)): the
  PostgreSQL `xmin` column is the concurrency token of each account. On a conflict, posting a transaction is
  retried up to 3 times with jittered backoff; if it still conflicts the API answers `409` with
  `code: concurrency_conflict`. Holds use the same token, so a hold cannot be captured and voided at the same time.

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
(`unbalanced_transaction`, `currency_mismatch`, `insufficient_funds`, `hold_not_pending`, `not_a_merchant_account`)
and `409` (`concurrency_conflict`, `hold_id_conflict`).
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

The hold endpoints (`POST /holds`, `GET /holds/{id}`, `POST /holds/{id}/capture` with a `merchantAccountId`,
`POST /holds/{id}/void`) are called by the Authorization API; the [Authorization API examples](#authorization-api)
show them at work.

## Authorization

The Authorization approves or declines card purchases and later captures or voids them. It keeps its own cards and
authorizations in its own database (`authorizationdb`) and leaves the money to the Ledger: an approved purchase is a
hold in the Ledger ([ADR 0007](docs/adr/0007-model-authorization-holds-in-the-ledger.md)). It uses the same layering
as the Ledger ([ADR 0004](docs/adr/0004-use-hexagonal-architecture-with-pragmatic-cqrs.md)):

| Project | What lives there | Depends on |
|---|---|---|
| `Authorization.Domain` | `Card`, `CardAuthorization`, decline reasons, `Money` and the invariants | nothing |
| `Authorization.Application` | Command/query handlers, idempotency rules, ports (repositories, idempotency key store, `ILedgerClient`) | Domain |
| `Authorization.Infrastructure` | EF Core + PostgreSQL adapters, migrations, Ledger HTTP client | Application |
| `Authorization.Api` | Minimal API endpoints, `Idempotency-Key` header, `Result` → `ProblemDetails`, composition root | Application, Infrastructure |

```mermaid
sequenceDiagram
    participant C as Client / POS
    participant A as Authorization API
    participant L as Ledger API
    C->>A: POST /authorizations with Idempotency-Key
    A->>A: claim the key, check card and currency
    A->>L: place hold (hold id = authorization id)
    L-->>A: hold pending, card balance reduced
    A-->>C: 201 approved
    C->>A: capture the authorization
    A->>L: capture hold to the merchant account
    L-->>A: hold captured, merchant credited
    A-->>C: 200 captured
```

- **Idempotency** ([ADR 0008](docs/adr/0008-make-purchase-authorization-idempotent.md)): every purchase carries an
  `Idempotency-Key` header, unique per purchase. The key is claimed in `authorizationdb` before anything else and gets
  an authorization id reserved for it, which is also the hold id. The same key with the same body replays the recorded
  response byte for byte, without a second hold; the same key with a different body is `422` `idempotency_key_reused`;
  while the first request is still running the answer is `409` `idempotency_request_in_progress`. Only authorizations
  (approved or declined) are recorded. After a `400` or `404` the key is released; after a `503` the next request with
  the key processes the purchase again with the same reserved id, so the Ledger returns the hold it may already have
  instead of placing a second one.
- **Declines** are business outcomes, not HTTP errors: an approved and a declined purchase both answer `201` and are
  recorded. Checks run in this order: input validation (`400`), card exists (`404` `card_not_found`), card active
  (declined `card_blocked`), currency equals the card's (declined `currency_mismatch`), hold placed in the Ledger
  (declined `insufficient_funds`). The merchant account is only checked for format at this point; the Ledger checks it
  on capture.
- **Capture and void** close an `approved` authorization: capture moves the held amount to its merchant account, void
  returns it to the card. They are not idempotent in the Ledger, so when an earlier attempt did reach the Ledger but its
  answer was lost, the next attempt gets `hold_not_pending`; the Authorization then reads the hold and **reconciles**:
  if the hold was completed the same way, the authorization is marked so and the call succeeds; if it was completed the
  other way, the authorization takes that status and the call is `422`. Retrying a capture or void is therefore safe
  without an idempotency key.

### When the Ledger is down

The Authorization talks to the Ledger through the `ILedgerClient` port, implemented by a typed `HttpClient` whose base
address `https+http://ledger-api` is resolved by Aspire service discovery. `ServiceDefaults` adds a standard resilience
handler with generic settings to every `HttpClient`; for the Ledger client that handler is **replaced** by a standard
handler tuned for authorization calls (section `Ledger` in configuration, see `LedgerClientOptions`):

| Setting | Value | Why |
|---|---|---|
| Total timeout | 2 s | a card authorization cannot wait long; the whole call, retries included, fits in it |
| Attempt timeout | 1 s | a slow attempt is abandoned while there is still budget for a retry |
| Retries | 2, exponential backoff from 100 ms with jitter | only for calls that are safe to repeat (see below) |
| Circuit breaker | standard defaults (opens at ≥ 10% failures over ≥ 100 attempts in 30 s, for 5 s) | stops hammering a Ledger that is down |

With these budgets, an attempt that times out leaves room for only one retry; both retries are used only by fast
failures (a `5xx`, a refused connection). Only safe calls are retried: reads (balance, hold) and placing a hold, which
is idempotent by hold id. Opening an account, capturing and voiding are never retried by the client: repeating them
would open a second account or fail with `hold_not_pending` (which the reconciliation above handles).

When the Ledger is down or slow (timeout, open circuit, connection error or a `5xx` after the retries), the
Authorization API answers `503 Service Unavailable` with `code: ledger_unavailable` within about 2 seconds instead of
hanging, and records nothing: no card is saved, no purchase is recorded (the idempotency key keeps its reserved id for
the retry), and a capture or void leaves the authorization `approved`. Errors returned by the Ledger keep their code
(e.g. `account_not_found`, `not_a_merchant_account`).

### Authorization API

With the platform running, the Authorization API listens on `http://localhost:5272` (the `http` endpoint of
`authorization-api` in the Aspire dashboard). The examples continue from the [Ledger API](#ledger-api) ones: they use
`$LEDGER` and the `$SETTLEMENT` account to load the card, and open a new merchant account to be paid:

```bash
AUTHZ=http://localhost:5272
MERCHANT_ID=$(curl -s -X POST "$LEDGER/accounts" -H 'Content-Type: application/json' \
  -d '{"type":"merchant","currency":"BRL"}' | jq -r .id)
```

Create a card. `POST /cards` opens the card's account in the Ledger, then saves the card (status `active`):

```bash
CARD_JSON=$(curl -s -X POST "$AUTHZ/cards" -H 'Content-Type: application/json' -d '{"currency":"BRL"}')
CARD_ID=$(echo "$CARD_JSON" | jq -r .id)
CARD_ACCOUNT=$(echo "$CARD_JSON" | jq -r .ledgerAccountId)
```

`201 Created`, `Location: /cards/{id}`:

```json
{"id":"<id>","ledgerAccountId":"<ledger account id>","currency":"BRL","status":"active"}
```

Load 100 into the card through the Ledger (debit settlement, credit the card's account):

```bash
curl -s -X POST "$LEDGER/transactions" -H 'Content-Type: application/json' -d '{
  "description": "Card load",
  "currency": "BRL",
  "entries": [
    { "accountId": "'"$SETTLEMENT"'", "direction": "debit", "amount": 100.00 },
    { "accountId": "'"$CARD_ACCOUNT"'", "direction": "credit", "amount": 100.00 }
  ]
}'
```

Authorize a purchase of 30 with a new `Idempotency-Key`:

```bash
KEY=$(uuidgen)
PURCHASE="{\"cardId\":\"$CARD_ID\",\"merchantAccountId\":\"$MERCHANT_ID\",\"amount\":30.00,\"currency\":\"BRL\"}"
AUTH=$(curl -s -X POST "$AUTHZ/authorizations" -H 'Content-Type: application/json' \
  -H "Idempotency-Key: $KEY" -d "$PURCHASE")
echo "$AUTH"
AUTH_ID=$(echo "$AUTH" | jq -r .id)
```

`201 Created`, `Location: /authorizations/{id}`:

```json
{"id":"<id>","status":"approved","declineReason":null,"cardId":"<card id>","merchantAccountId":"<merchant id>","amount":30.00,"currency":"BRL","createdAt":"<timestamp>","completedAt":null}
```

Send the same purchase again with the same key, as a POS would after a timeout. The answer is the same `201` with
exactly the same body (same id), and no second hold is placed:

```bash
curl -s -X POST "$AUTHZ/authorizations" -H 'Content-Type: application/json' \
  -H "Idempotency-Key: $KEY" -d "$PURCHASE"
```

The same key with a different body is rejected with `422` `idempotency_key_reused`:

```bash
curl -s -X POST "$AUTHZ/authorizations" -H 'Content-Type: application/json' -H "Idempotency-Key: $KEY" \
  -d "{\"cardId\":\"$CARD_ID\",\"merchantAccountId\":\"$MERCHANT_ID\",\"amount\":50.00,\"currency\":\"BRL\"}"
```

The 30 are held, so the card balance in the Ledger is now 70:

```bash
curl -s "$LEDGER/accounts/$CARD_ACCOUNT/balance"
```

```json
{"accountId":"<card account>","balance":{"amount":70.00,"currency":"BRL"}}
```

Capture the authorization (no body). The held 30 go to the merchant account:

```bash
curl -s -X POST "$AUTHZ/authorizations/$AUTH_ID/capture"
curl -s "$LEDGER/accounts/$MERCHANT_ID/balance"
```

`200 OK` with the authorization, now `"status":"captured"` with a `completedAt` timestamp; the merchant balance is 30
and the card balance stays 70. `GET /authorizations/{id}` returns the same authorization.

A second purchase, this time voided: the held 20 go back to the card, whose balance returns to 70:

```bash
AUTH2_ID=$(curl -s -X POST "$AUTHZ/authorizations" -H 'Content-Type: application/json' \
  -H "Idempotency-Key: $(uuidgen)" \
  -d "{\"cardId\":\"$CARD_ID\",\"merchantAccountId\":\"$MERCHANT_ID\",\"amount\":20.00,\"currency\":\"BRL\"}" | jq -r .id)
curl -s -X POST "$AUTHZ/authorizations/$AUTH2_ID/void"
curl -s "$LEDGER/accounts/$CARD_ACCOUNT/balance"
```

A purchase above the balance is declined, still with `201` and recorded:

```bash
curl -s -X POST "$AUTHZ/authorizations" -H 'Content-Type: application/json' -H "Idempotency-Key: $(uuidgen)" \
  -d "{\"cardId\":\"$CARD_ID\",\"merchantAccountId\":\"$MERCHANT_ID\",\"amount\":500.00,\"currency\":\"BRL\"}"
```

```json
{"id":"<id>","status":"declined","declineReason":"insufficient_funds","cardId":"<card id>","merchantAccountId":"<merchant id>","amount":500.00,"currency":"BRL","createdAt":"<timestamp>","completedAt":null}
```

Block the card (blocking a card that is already blocked also succeeds) and read it back; both answer `200 OK` with
`"status":"blocked"`, and from now on its purchases are declined with `card_blocked`:

```bash
curl -s -X POST "$AUTHZ/cards/$CARD_ID/block"
curl -s "$AUTHZ/cards/$CARD_ID"
```

Errors are `application/problem+json` with a machine-readable `code`:

| Status | Codes |
|---|---|
| `400` | `idempotency_key_required`, `invalid_idempotency_key`, `invalid_card_id`, `invalid_merchant_account_id`, `invalid_currency`, `invalid_amount`, `too_many_decimal_places` |
| `404` | `card_not_found`, `authorization_not_found`, `account_not_found` (unknown merchant account, on capture) |
| `409` | `idempotency_request_in_progress` |
| `422` | `idempotency_key_reused`, `authorization_not_capturable`, `authorization_not_voidable`, `not_a_merchant_account` (on capture) |
| `503` | `ledger_unavailable` |

Only `approved` authorizations can be captured or voided: a declined, captured or voided one is `422`
`authorization_not_capturable` / `authorization_not_voidable` and the Ledger is not called.

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
  CardPlatform.Authorization.Api.Tests/ Integration tests (WebApplicationFactory + Testcontainers PostgreSQL, Ledger API in process)
docs/adr/                             Architecture Decision Records
```

## Roadmap

- [x] Solution skeleton, Aspire orchestration, CI
- [x] Ledger: double-entry accounts and transactions (EF Core + PostgreSQL)
- [x] Authorization API: idempotent authorization flow with holds, capture and void
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
7. [Model authorization holds in the ledger](docs/adr/0007-model-authorization-holds-in-the-ledger.md)
8. [Make purchase authorization idempotent](docs/adr/0008-make-purchase-authorization-idempotent.md)
