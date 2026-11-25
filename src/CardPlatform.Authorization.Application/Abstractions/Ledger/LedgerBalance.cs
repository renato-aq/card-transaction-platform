namespace CardPlatform.Authorization.Application.Abstractions.Ledger;

/// <summary>The current balance of a Ledger account.</summary>
public sealed record LedgerBalance(Guid AccountId, decimal Amount, string Currency);
