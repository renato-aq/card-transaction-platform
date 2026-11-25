# 5. Use a double-entry ledger model

**Status:** Accepted

## Context

The Ledger is the source of truth for every card balance. Storing a single mutable balance per card
makes it impossible to explain where money came from or went, and a bug can create or destroy money
silently. Accounting solved this long ago with double-entry bookkeeping.

## Decision

Model the Ledger with **accounts**, **transactions** and **entries**, with the invariants enforced by
the domain (`CardPlatform.Ledger.Domain`):

- An `Account` has a type (`Card`, `Merchant`, `Settlement`), a currency and a balance, and is opened
  with a zero balance.
- A `LedgerTransaction` has at least two entries; each entry debits or credits one account with a
  positive amount; all entries share one currency; and **the sum of debits equals the sum of credits**.
  Violations are returned as a `Result` (`insufficient_entries`, `invalid_amount`, `mixed_currencies`,
  `unbalanced_transaction`).
- Applying a transaction is **all or nothing**: every account is checked (`currency_mismatch`,
  `insufficient_funds`) before any balance changes. An account in several entries receives the net effect.

**Sign convention:** every account, whatever its type, uses `balance = credits − debits`. Because each
transaction is balanced, **the sum of all balances is always zero**, which is a cheap integrity check.

Top-up of 100 (money enters the platform):

| Account | Debit | Credit | Balance |
|---|---|---|---|
| Settlement | 100 | | −100 |
| Card | | 100 | 100 |

Purchase of 30:

| Account | Debit | Credit | Balance |
|---|---|---|---|
| Card | 30 | | 70 |
| Merchant | | 30 | 30 |

Settlement of the merchant (money leaves the platform):

| Account | Debit | Credit | Balance |
|---|---|---|---|
| Merchant | 30 | | 0 |
| Settlement | | 30 | −70 |

A **`Card` account can never be negative** (`insufficient_funds`): it represents money the cardholder
actually has. `Merchant` and `Settlement` accounts can be negative; a negative `Settlement` balance is
the money held on behalf of cardholders and merchants.

## Consequences

- Every balance is explained by its entries, and money cannot be created or destroyed by a single write.
- The rules are unit-tested without a database; dates are passed in, so tests are deterministic.
- Every movement writes at least two entries, so storage grows faster than with a single balance column.
- Reading "available balance" with the `credits − debits` convention is natural for cards but means the
  platform-side accounts usually show negative balances.
- Holds (reservations) and credit limits are not modelled yet and will need their own decision.
