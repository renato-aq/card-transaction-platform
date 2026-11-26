namespace CardPlatform.Fraud.Domain.Common;

/// <summary>Kind of an expected failure, so adapters can react to an <see cref="Error"/> without knowing every code.</summary>
public enum ErrorType
{
    /// <summary>The input is malformed or out of range (e.g. an invalid currency code).</summary>
    Validation,

    /// <summary>The operation conflicts with the current state (e.g. capturing a voided purchase).</summary>
    Conflict,
}
