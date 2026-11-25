# 9. Fail fast when the Ledger is unavailable

**Status:** Accepted

## Context

A card authorization is synchronous: a POS or acquirer waits for an approve/decline answer and gives up after a few
seconds. The Authorization cannot answer on its own, because the Ledger is the source of truth for balances and every
purchase, capture and void goes through a hold there (ADR 0007). The Ledger can be slow, down or overloaded, and the
Authorization must answer within a predictable time instead of hanging.

`ServiceDefaults` (ADR 0002) gives every `HttpClient` a standard resilience handler (`ConfigureHttpClientDefaults`)
with generic defaults: a 10 s timeout per attempt, a 30 s total timeout and 3 retries with exponential backoff. Those
are too slow for a payment path, and the default retry repeats any request, including ones that are not safe to repeat.

## Decision

**A tuned standard handler for the `ledger` client only.** The typed `HttpClient` behind `ILedgerClient` removes the
inherited resilience handler (`RemoveAllResilienceHandlers()`) and adds its own standard handler, configured from the
`Ledger` configuration section (`LedgerClientOptions`, applied by `LedgerRequestPolicy`). The handler is moved back to
the outside of the pipeline, before service discovery, as `ServiceDefaults` orders them, so each retry resolves the
endpoint again and the timeouts cover the whole call. Other clients keep the global defaults.

| Setting | Value |
|---|---|
| Attempt timeout | 1 s |
| Total timeout (retries included) | 2 s |
| Retries | 2, exponential backoff from 100 ms with jitter |
| Circuit breaker | standard defaults |

**Retries only for safe calls.** The default transient-failure predicate (network errors, timeouts, `408`, `429`,
`5xx`) is kept, but only for reads (`GET`: balance, hold) and requests the client marks as idempotent: placing a hold,
idempotent by hold id. Opening an account, capturing and voiding are never retried by the client: repeating them would
open a second account or fail with `hold_not_pending`.

**Communication failures become `ledger_unavailable`.** A timeout, an open circuit, a network error, or a `5xx`, `408`
or `429` still returned after the retries is translated by `LedgerHttpClient` into the `ledger_unavailable` error
(type `Unavailable`), which the API answers with **`503 Service Unavailable`**. Ledger `4xx` problems (`400`, `404`,
`409`, `422`) keep their code and type, so the client sees the Ledger's reason (e.g. `account_not_found`,
`not_a_merchant_account`); `insufficient_funds` on placement becomes a declined purchase. Any other status, or a
problem without a code, is a contract bug and throws.

**Nothing is approved on a `503`.** No purchase is recorded and a capture or void leaves the authorization `approved`.
The outcome is settled by the next attempt: a retried purchase with the same `Idempotency-Key` reuses the reserved hold
id, so the Ledger returns the hold it may already have instead of placing a second one (ADR 0008), and a retried
capture or void reconciles with the hold's final state (ADR 0007).

**Alternatives considered:**

- **Keep the global defaults:** a slow Ledger would hold a purchase for up to 30 s, far beyond what a POS waits.
- **Change the global defaults:** would affect every `HttpClient` of every service, whose needs differ.
- **Queue the authorization and answer asynchronously:** does not fit a purchase that needs an approve/decline answer
  in the same request; asynchronous messaging is kept for integration events (ADR 0003).

## Consequences

- Predictable worst-case latency: a call to the Ledger ends within about 2 s, and the client gets a clear `503` it can
  act on instead of a hanging connection.
- With these budgets an attempt that times out leaves room for only one retry; both retries are used only by fast
  failures (a `5xx`, a refused connection).
- The circuit breaker stops sending requests to a Ledger that keeps failing, which protects it while it recovers, at
  the cost of failing fast for a few seconds after it is back.
- Clients must retry a `503` with the same `Idempotency-Key` (purchases) or simply repeat the capture or void; the
  platform guarantees those retries are safe.
- `RemoveAllResilienceHandlers()` is an experimental API (`EXTEXP0001`, warning suppressed locally around the call):
  it is the supported way to override, for one client, the handler added to all of them, but it may change in a future
  version of `Microsoft.Extensions.Http.Resilience`.
- The timeouts and retries are configuration, not code, so they can be tuned per environment without a release.
