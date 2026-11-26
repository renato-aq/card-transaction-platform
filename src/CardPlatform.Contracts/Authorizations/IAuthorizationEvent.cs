namespace CardPlatform.Contracts.Authorizations;

/// <summary>
/// An Authorization decision, published on <see cref="Topics.Authorizations"/>. Carries what fraud detection needs
/// about the purchase.
/// </summary>
public interface IAuthorizationEvent : IIntegrationEvent
{
    /// <summary>Id of the authorization (UUID v7).</summary>
    Guid AuthorizationId { get; }

    /// <summary>The merchant, identified by its Ledger account id.</summary>
    Guid MerchantAccountId { get; }

    /// <summary>Purchase amount, with at most two decimal places.</summary>
    decimal Amount { get; }

    /// <summary>ISO 4217 currency code (three uppercase letters).</summary>
    string Currency { get; }

    /// <summary>When the purchase was authorized (the authorization's creation time).</summary>
    DateTimeOffset CreatedAt { get; }
}
