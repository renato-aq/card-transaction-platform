# 6. Use optimistic concurrency for ledger accounts

**Status:** Accepted

## Context

Posting a transaction reads the accounts, applies the domain rules (`insufficient_funds`, all or nothing) and
writes the new balances. Two simultaneous purchases on the same card can read the same balance: without
protection, one write overwrites the other (a lost update), so balances stop matching the entries and a card
could spend money it does not have.

PostgreSQL offers two ways to prevent this:

- **Pessimistic locking** (`SELECT ... FOR UPDATE`): the accounts are locked when read and other requests wait
  until the transaction ends. It needs an explicit database transaction around the whole use case, ties the
  Application layer to the locking strategy, and holds locks while application code runs.
- **Optimistic concurrency**: nothing is locked; each update checks that the row did not change since it was
  read, and fails if it did.

## Decision

Use **optimistic concurrency** on `accounts`, with the PostgreSQL **`xmin`** system column as the token:

- `xmin` changes on every update of a row, so no extra column or trigger is needed and the database maintains it.
  It is mapped in Infrastructure as an EF Core shadow `uint` property with `IsRowVersion()`; the domain model is
  unaware of it (ADR 0004). Every account touched by a transaction is checked, including `Merchant` and
  `Settlement` accounts.
- The EF Core adapter translates `DbUpdateConcurrencyException` into the Application's
  `ConcurrencyConflictException`, so the Application layer does not depend on EF Core. Nothing is saved when it
  happens.
- A **retry decorator** wraps the `PostTransaction` command handler: on a conflict it discards the tracked
  changes, waits a random delay (exponential backoff with full jitter: up to 50, 100 and 200 ms) and runs the
  handler again, which reloads the accounts and re-applies the rules to their current balances. It retries **at
  most 3 times** (4 attempts).
- When the retries are exhausted the API answers **`409 Conflict`** with `code: concurrency_conflict`; the client
  may try again. Business failures (`insufficient_funds`, …) and unexpected exceptions are never retried.

Optimistic concurrency fits the Ledger: transactions are short, contention on a single card is rare in practice,
no lock is held while application code runs, and it works with the EF Core unit of work without explicit
transactions.

## Consequences

- Simultaneous requests never produce a wrong balance or a negative card: either the write is based on the
  current balance or it is retried.
- Under heavy contention on the same account a request can still fail with `409 concurrency_conflict`, and each
  retry adds latency.
- Hot accounts shared by many cards (`Merchant`, `Settlement`) take part in every purchase and will see more
  conflicts as volume grows. If that becomes a problem, alternatives are pessimistic locking for those accounts,
  queueing writes per account, or not updating platform-side balances synchronously; that needs its own decision.
- Other commands do not retry on conflicts yet; each must opt in to the decorator explicitly.
