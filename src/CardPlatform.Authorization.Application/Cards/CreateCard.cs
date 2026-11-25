namespace CardPlatform.Authorization.Application.Cards;

/// <summary>
/// Creates an active card with its own card account in the Ledger. <paramref name="Currency"/> arrives as raw input
/// and is validated by the handler (ISO 4217 code).
/// </summary>
public sealed record CreateCard(string? Currency);
