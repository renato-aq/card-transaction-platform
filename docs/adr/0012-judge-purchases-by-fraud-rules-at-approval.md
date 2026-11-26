# 12. Judge purchases by fraud rules at approval

**Status:** Accepted

## Context

The Fraud Worker keeps a purchase history per card from the Authorization's events (ADR 0011). It must detect
suspicious purchases with two rules, velocity (N or more approved purchases of a card in a time window) and amount
above average (more than X times the average of the card's previous approved purchases in the same currency, given a
minimum history), and publish a `FraudSuspected` reliably, at most once per authorization even when both rules fire.
The contract carries the rule and the reason as single strings. Events can be delivered more than once and the topic
can be replayed into a new database.

## Decision

**Rules are domain objects.** `VelocityRule`, `AmountAboveAverageRule` and `FraudRules` (in
`CardPlatform.Fraud.Domain.Rules`) judge a purchase against a `CardActivity`: how many approved purchases of the card
fall in the velocity window before it, and the count and sum of its previous approved purchases in the purchase's
currency. Only approved purchases count, captured and voided ones included (they were approved); declined ones never.
The judged purchase is never part of its own activity.

**The purchase's own time is the clock.** The window is `(t − window, t]` where `t` is the purchase's
`AuthorizedAt`, and "previous" means authorized at or before `t`, not the time the message is processed. A purchase
is therefore judged the same way however late it is consumed or replayed. Purchases of the card authorized after `t`
(possible only for two purchases of the card racing in the Authorization, ADR 0010) are not counted for it.

**Thresholds.** Velocity fires when the count, the judged purchase included, is at least the threshold, so N − 1
purchases do not fire it. The amount rule fires when the amount is strictly greater than X times the average,
compared as `amount × count > X × total` so no rounding decides it, and only with at least the minimum history. The
limits come from `Fraud:Rules` (`Velocity:Threshold=5`, `Velocity:Window=00:01:00`,
`AmountAboveAverage:Multiplier=3`, `AmountAboveAverage:MinimumHistory=5`), are validated on startup
(`FraudRulesOptions`, in Infrastructure) and handed to the domain as a `FraudRules` value; the domain has no
configuration dependency.

**One suspicion per purchase, rules joined.** A purchase that fires any rule raises one `FraudSuspicion` listing every
rule that fired. `FraudSuspected.Rule` is the rule codes joined by commas in evaluation order (`velocity`,
`amount_above_average`, or `velocity,amount_above_average`); `Reason` joins their reasons, one sentence each. Codes are
stable snake_case strings and part of the contract.

**Judged once, at approval, in the processing transaction.** `RecordApprovedPurchaseHandler` judges a purchase only
when it records it for the first time; an approval already recorded is not judged again, and declines, captures and
voids are never judged (the approval came first, ADR 0010; a capture or void whose approval expired from the topic is
an old purchase). The suspicion is written through the Application's `IOutbox` port to the Fraud Worker's
`outbox_messages`, by the same `SaveChanges` and in the same transaction as the purchase and the message's processed
mark (ADR 0011), and published on `fraud-signals` with key = card id by the shared outbox publisher (ADR 0010). Its
event id is a name-based UUID of the authorization id and the event type (`NameBasedGuid`, moved to
`CardPlatform.Contracts` so both services share it), so even if an approval were judged twice (a database rebuilt and
replayed) the copies carry the same id and the consumers keep one. `DetectedAt` is the processing time.

**Bounded reads.** The handler does not load the card's whole history: the repository computes the `CardActivity` with
two aggregate queries on the index `(card_id, authorized_at)`, a count over the window and a count and sum in the
currency, with the same definition as `CardActivity.Of(history)`, which the unit tests use.

## Consequences

- Rules are pure and unit-tested at their limits; the processing time never changes an outcome.
- A redelivered or duplicated approval produces no second suspicion: the processed mark, the already-recorded check
  and the deterministic event id each prevent it.
- Changing the limits only affects purchases judged afterwards; past purchases are not judged again.
- The average reads every previous approved purchase of the card in the currency (filtered by the card's index); a
  running aggregate per card and currency would be the next step if histories grow large.
- Two purchases of a card authorized at nearly the same time and consumed out of order may not count each other.
