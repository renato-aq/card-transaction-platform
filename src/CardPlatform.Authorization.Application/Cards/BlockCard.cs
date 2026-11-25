namespace CardPlatform.Authorization.Application.Cards;

/// <summary>
/// Blocks a card. Blocking a card that is already blocked succeeds. Fails with
/// <see cref="CardErrors.CardNotFoundCode"/> when the card does not exist.
/// </summary>
public sealed record BlockCard(Guid CardId);
