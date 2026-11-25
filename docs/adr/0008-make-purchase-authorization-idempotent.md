# 8. Make purchase authorization idempotent

**Status:** Accepted

## Context

A POS or acquirer that does not get an answer to `POST /authorizations` (timeout, dropped connection) sends the same
purchase again. Without protection, each attempt is a new authorization with a new hold in the Ledger, and the
cardholder is charged twice.

The Ledger is already idempotent by hold id (ADR 0007): placing a hold with an existing id and the same data returns
the existing hold. That only helps if a retry of the purchase uses the **same** id. Before this decision the
Authorization created a new id per request, so a hold placed by an attempt whose reply was lost (the client saw
`ledger_unavailable`, `503`) stayed pending and the retry placed a second one.

The client therefore has to identify the purchase, the Authorization has to remember what it did for that identifier,
and the hold id has to stay the same across attempts, including after a `503`.

## Decision

`POST /authorizations` requires an **`Idempotency-Key`** header (at most 255 characters). A missing or blank key is
`400` `idempotency_key_required`; a longer one is `400` `invalid_idempotency_key`.

**Where.** Keys live in the Authorization's own PostgreSQL database (`authorizationdb`), table `idempotency_keys`:
the key (primary key), the SHA-256 of the request, the authorization id reserved for it, the current claim (token and
lease end), the response (HTTP status code and body, as `text`, so it is replayed byte for byte) and the dates. The
database already owns the authorizations, so the key and the authorization can be written in one transaction and the
primary key gives uniqueness without a distributed lock or another store (e.g. Redis) to keep consistent.

**Request hash.** SHA-256 (lowercase hex) of a canonical JSON of the contract fields: properties in alphabetical order
(`amount`, `cardId`, `currency`, `merchantAccountId`), no whitespace, every value a string or `null`; GUIDs in the
`D` format, the amount without trailing zeros (`30.00` → `"30"`) and the currency exactly as sent. It is computed from
the deserialized request, so property order, whitespace and the case of GUIDs do not change it. Properties outside
the contract are ignored by the API when it reads the body, so they do not change it either: a repetition that only
adds an unknown property is the same request.

**Claim first, then process.** The handler claims the key before doing anything else, with a single statement in its
own short transaction:

```sql
INSERT INTO idempotency_keys (key, request_hash, authorization_id, claim_token, locked_until, created_at) VALUES (...)
ON CONFLICT (key) DO UPDATE SET claim_token = excluded.claim_token, locked_until = excluded.locked_until
WHERE idempotency_keys.response_status_code IS NULL
  AND idempotency_keys.request_hash = excluded.request_hash
  AND (idempotency_keys.locked_until IS NULL OR idempotency_keys.locked_until <= excluded.created_at)
RETURNING *
```

- A new key is inserted with a **new authorization id (UUID v7) reserved for it**. An existing key that has no response,
  the same hash and no live claim is taken over and **keeps its reserved id**.
- When no row comes back the key could not be claimed, nothing is processed and the Ledger is not called: a different
  hash is `422` `idempotency_key_reused`; a stored response is returned as it is; otherwise another request holds the
  claim and the answer is `409` `idempotency_request_in_progress`, and the client tries again later.
- The purchase is then authorized with the reserved id as authorization id and hold id. The Ledger call is not inside
  a database transaction.
- An authorization (approved or declined, both `201`) is saved **in one transaction with the key's response**: the
  wrapped `AuthorizePurchase` handler gets a unit of work that defers its save, and the idempotency handler saves the
  authorization and the completed key together with the real one. The completion is an
  `UPDATE ... WHERE claim_token = <own token>` (EF Core concurrency token), so a request that lost its claim cannot
  overwrite another one's response.

An `INSERT ... ON CONFLICT` claim was chosen over inserting the key and catching the unique violation because it also
covers taking over a released or expired claim in the same atomic statement, and over a transaction with
`SELECT ... FOR UPDATE` held around the whole purchase because that would keep a connection and a row lock while the
Ledger is called.

**Concurrent requests.** Of several requests with the same new key, PostgreSQL lets one insert it and makes the others
wait for that commit and take the conflict path; they see a live claim and get `409` (or the stored response once the
first one finished). Exactly one request calls the Ledger.

**What is stored.** Only authorizations, approved or declined (`201`), together with their response. Failures are
never stored:

- `400` and `404` (`invalid_*`, `card_not_found`): the request was rejected before any hold could exist, so the key is
  deleted and can be used again, even with a corrected body.
- `503` `ledger_unavailable` and any other failure or exception: the hold may have been placed, so the claim is only
  ended. The key keeps its hash and **its reserved authorization id**; the next request with the key processes the
  purchase again with the same id, and the Ledger answers with the hold it already has instead of placing a second
  one.

**Lease.** A claim lasts 30 seconds, much longer than a purchase's time budget (the Ledger client gives up after a few
seconds). A request that crashes or hangs without ending its claim blocks its key with `409` only until then; the next
request takes it over with the same reserved id. If the old request still finishes, its completion fails (the claim
token no longer matches, or the authorization id is already saved) and it answers with what the key holds. Keys are
not expired or cleaned up yet.

**Layers (ADR 0004).** The API reads the header; `IdempotentAuthorizePurchaseHandler` (Application) wraps the
`AuthorizePurchase` handler and holds the rules above; `IIdempotencyKeyStore` is an Application port implemented with
EF Core in Infrastructure. The stored response is produced by the API through an Application port
(`IAuthorizationResponseFactory`) and kept by the Application as an opaque status code and body, so no HTTP type
reaches the Application. The first answer and every replay are written from the stored body; `Location` is rebuilt
from the authorization id.

**Relation to the Ledger.** Two levels of idempotency, each with its own job: the Authorization deduplicates purchases
by `Idempotency-Key` and turns the key into a stable hold id; the Ledger deduplicates holds by that id, which also
makes the Ledger client's own retries of placing a hold safe (ADR 0007).

## Consequences

- The same purchase sent several times results in one authorization and one hold, even when an answer was lost after
  the hold was placed, and the client gets the same response every time.
- Clients must send a unique key per purchase and reuse it only to repeat that purchase; a key reused for another
  purchase is rejected.
- Every authorization costs one extra statement (the claim) before the work and one extra row; the table grows with
  every purchase until key expiry is added.
- A request that crashes blocks its key for up to the lease (30 seconds); a request that lives longer than the lease
  may see another request process the same key, which is safe because both use the same hold id and only one can save.
- Responses are stored as text produced by the API: changing the response contract does not change responses that were
  already stored.
