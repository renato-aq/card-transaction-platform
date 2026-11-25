using CardPlatform.Ledger.Domain.Common;

namespace CardPlatform.Ledger.Application.Concurrency;

/// <summary>Expected failures caused by concurrent requests (ADR 0006).</summary>
public static class ConcurrencyErrors
{
    public const string ConcurrencyConflictCode = "concurrency_conflict";

    /// <summary>The command kept conflicting with concurrent requests after every retry; nothing was saved.</summary>
    public static readonly Error ConcurrencyConflict = Error.Conflict(
        ConcurrencyConflictCode,
        "The accounts were changed by concurrent requests and the operation could not be completed. Try again.");
}
