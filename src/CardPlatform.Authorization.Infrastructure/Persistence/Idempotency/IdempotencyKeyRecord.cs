namespace CardPlatform.Authorization.Infrastructure.Persistence.Idempotency;

/// <summary>
/// A row of <c>idempotency_keys</c> (ADR 0008). It is a persistence record, not a domain entity: it holds the hash of
/// the first request, the authorization id reserved for the key, the current claim (token and lease) and, once the
/// request is processed, the response (status code and body).
/// </summary>
public sealed class IdempotencyKeyRecord
{
    public required string Key { get; set; }

    /// <summary>SHA-256 of the canonical request, lowercase hex.</summary>
    public required string RequestHash { get; set; }

    /// <summary>Reserved when the key is first claimed; also the hold id in the Ledger.</summary>
    public Guid AuthorizationId { get; set; }

    /// <summary>Token of the request that holds the claim; <see langword="null"/> when no request does.</summary>
    public Guid? ClaimToken { get; set; }

    /// <summary>End of the claim's lease; <see langword="null"/> when the key is not claimed.</summary>
    public DateTimeOffset? LockedUntil { get; set; }

    public int? ResponseStatusCode { get; set; }

    /// <summary>The response body exactly as it was sent (<c>text</c>, not <c>jsonb</c>, so it is not reformatted).</summary>
    public string? ResponseBody { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}
