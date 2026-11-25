namespace CardPlatform.Authorization.Application.Cards;

/// <summary>
/// Reads a card and its status. Fails with <see cref="CardErrors.CardNotFoundCode"/> when the card does not exist.
/// The handler lives in Infrastructure because it reads the database directly.
/// </summary>
public sealed record GetCard(Guid CardId);
