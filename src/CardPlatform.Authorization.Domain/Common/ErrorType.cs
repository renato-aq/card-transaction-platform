namespace CardPlatform.Authorization.Domain.Common;

/// <summary>
/// Kind of an expected failure. It lets inbound adapters translate an <see cref="Error"/> (for example to an
/// HTTP status code) without knowing every error code.
/// </summary>
public enum ErrorType
{
    /// <summary>The input is malformed or out of range (e.g. an invalid currency code).</summary>
    Validation,

    /// <summary>A referenced resource does not exist (e.g. an unknown account).</summary>
    NotFound,

    /// <summary>The input is well formed but breaks a business rule (e.g. insufficient funds).</summary>
    BusinessRule,

    /// <summary>The operation conflicts with the current state (e.g. a concurrent update).</summary>
    Conflict,

    /// <summary>A dependency the operation needs could not be reached in time (e.g. the Ledger is down).</summary>
    Unavailable,
}
