namespace CardPlatform.Authorization.Application.Abstractions.Ledger;

/// <summary>An account opened in the Ledger and its balance.</summary>
public sealed record LedgerAccount(Guid Id, string Currency, decimal Balance);
