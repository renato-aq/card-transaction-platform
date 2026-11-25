namespace CardPlatform.Authorization.Domain.Cards;

/// <summary>Status of a <see cref="Card"/>: purchases are only authorized while it is <see cref="Active"/>.</summary>
public enum CardStatus
{
    Active,
    Blocked,
}
