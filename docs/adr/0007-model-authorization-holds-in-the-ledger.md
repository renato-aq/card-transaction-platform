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
