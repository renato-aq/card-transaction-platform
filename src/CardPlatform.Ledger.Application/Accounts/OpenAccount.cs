namespace CardPlatform.Ledger.Application.Accounts;

/// <summary>
/// Opens an account with a zero balance. Both fields arrive as raw input and are validated by the handler:
/// <paramref name="Type"/> is an <see cref="Domain.Accounts.AccountType"/> name (case-insensitive) and
/// <paramref name="Currency"/> an ISO 4217 code.
/// </summary>
public sealed record OpenAccount(string? Type, string? Currency);
