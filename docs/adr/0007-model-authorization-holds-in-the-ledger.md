# 7. Model authorization holds in the ledger

**Status:** Accepted

## Context

A card purchase is authorized first and captured later (minutes or days after). Between the two, the authorized
amount must no longer be spendable by the cardholder, otherwise two purchases could authorize the same money. It
must not reach the merchant either: the authorization can still be voided, and only a capture means the merchant is
owed the money. ADR 0005 left holds out of the model.

Two ways to represent a hold were considered:

- **A field on the card account** ("reserved amount", with available = balance − reserved): the card balance would
  no longer be explained by its entries alone, and every rule that reads the balance would need to know about it.
- **A double-entry transaction to a dedicated account**: the hold is ordinary money movement, so the existing
  invariants (balanced, positive, single currency, card never negative, all or nothing) apply unchanged.

## Decision

Model a hold as **double-entry transactions through a `Holds` account**:

- A new account type **`Holds`**: one per currency (created on first use by the Application), allowed to have any
  balance. Its balance is the sum of pending holds in that currency.
- A **`Hold`** entity in the Domain: an id given by the creator (the Authorization service uses the authorization
  id, which makes the operations idempotent), the card account, the amount (`Money`), the status, the creation and
  completion dates, and the ids of the placement and completion transactions (for audit).
- Each operation builds a `LedgerTransaction` and applies it with `ApplyTo`, returning a `Result`; the hold changes
  state only when the transaction was applied.

Place a hold of 30 (card balance 100):

| Account | Debit | Credit | Balance |
|---|---|---|---|
| Card | 30 | | 70 |
| Holds | | 30 | 30 |

Capture it to a merchant:

| Account | Debit | Credit | Balance |
|---|---|---|---|
| Holds | 30 | | 0 |
| Merchant | | 30 | 30 |

Or void it, returning the money to the card:

| Account | Debit | Credit | Balance |
|---|---|---|---|
| Holds | 30 | | 0 |
| Card | | 30 | 100 |

**State machine:** `Pending → Captured` or `Pending → Voided`; both are final. Capturing or voiding a hold that is
not `Pending` returns `hold_not_pending` (business rule). Placing fails with `insufficient_funds` when the card
would go negative; the holds and merchant accounts must be in the hold's currency (`currency_mismatch`). A capture
is always for the whole held amount.

The merchant account comes from the client, so a non-`Merchant` account is a business error
(`not_a_merchant_account`). The card and holds accounts are chosen by the Application, so a wrong account type there
is a programming error and throws.

Holds are stored in the `holds` table with the PostgreSQL `xmin` column as concurrency token, as accounts are
(ADR 0006), so two requests cannot both capture or void the same hold.

## Consequences

- The card balance is the available balance: held money cannot be spent twice, and every balance is still explained
  by its entries; the sum of all balances stays zero.
- A purchase writes two transactions (place and capture) instead of one, and the `Holds` account of each currency
  takes part in every authorization, becoming a hot account for optimistic concurrency (see ADR 0006).
- Holds that are never captured keep the money reserved; automatic expiry, partial captures and captures for a
  different amount are not modelled yet and will need their own decision.

## Amendment (2026-11-25)

Implementing the Authorization service on top of holds refined this decision; the original text above is kept as it
was written.

**Card account type.** The card account of `POST /holds` comes from the client too (the Authorization sends the card's
Ledger account id), so the Application checks it after loading the account and returns `not_a_card_account` (business
rule, `422`) for any other type, as it does for the merchant account. The domain (`Hold.Place`) still throws for a card
or holds account of the wrong type, which is now only reachable through a programming error.

**How the Authorization uses holds.** The hold id is the authorization id, reserved before the Ledger is called
(ADR 0008), so placing a hold is idempotent across retries. Capturing and voiding are **not** idempotent in the Ledger:
a hold that is no longer pending answers `hold_not_pending`, whether an earlier attempt completed it and its reply was
lost or a concurrent request completed it first. The Authorization therefore **reconciles**
(`AuthorizationReconciliation`): on `hold_not_pending` it reads the hold (`GET /holds/{id}`) and adopts the Ledger's
final state, which is the source of truth:

- the hold is in the requested state: the authorization is completed the same way and the call answers `200`, as if
  this attempt had done it;
- the hold is in the other final state: the authorization takes that status and the call answers `422`
  `authorization_not_capturable` / `authorization_not_voidable`, as if the local status had been up to date;
- the hold cannot be read (e.g. `ledger_unavailable`) or is still pending: the error is returned and the authorization
  is unchanged, so a later attempt reconciles it.

**No concurrency token on `authorizations`.** Every write of a completed status follows a final hold state reported by
the Ledger, and the Ledger serializes captures and voids of a hold through its `xmin` (ADR 0006): only one request
completes the hold, every other one gets `hold_not_pending` and reconciles to the same final state, which never changes
again. Concurrent writers of an authorization therefore write the same status and a last-writer-wins update converges.
Only `completedAt` is last-writer-wins: it comes from the Authorization's clock, so at worst it holds the time of the
last request that recorded the completion.

**Known limitation.** A hold whose authorization was never recorded stays pending: the hold was placed but the reply
was lost and the client never retries with the same `Idempotency-Key`, or the Authorization crashed before saving. The
money stays reserved until someone voids the hold in the Ledger. Expiry of pending holds and reconciliation of holds
without an authorization remain out of scope (see ADR 0008).
