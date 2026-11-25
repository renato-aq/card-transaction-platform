namespace CardPlatform.Ledger.Application.Common;

/// <summary>Monetary amount returned by use cases: an amount with two decimal places and an ISO 4217 code.</summary>
public sealed record MoneyDto(decimal Amount, string Currency);
