namespace CardPlatform.Ledger.Application.Accounts;

/// <summary>
/// Reads the current balance of an account. Fails with <see cref="AccountErrors.AccountNotFoundCode"/> when the
/// account does not exist. The handler lives in Infrastructure because it reads the database directly.
/// </summary>
public sealed record GetAccountBalance(Guid AccountId);
