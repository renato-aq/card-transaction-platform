namespace CardPlatform.Fraud.Domain.Rules;

/// <summary>How many approved purchases a card made in one currency and how much they add up to.</summary>
/// <param name="Count">How many purchases; zero or more.</param>
/// <param name="Total">The sum of their amounts, in the currency of the summary; zero when there is none.</param>
public sealed record SpendingSummary(int Count, decimal Total)
{
    /// <summary>No purchases.</summary>
    public static SpendingSummary None { get; } = new(0, 0m);

    public int Count { get; } = Count >= 0
        ? Count
        : throw new ArgumentOutOfRangeException(nameof(Count), Count, "The count of purchases cannot be negative.");

    public decimal Total { get; } = Total >= 0
        ? Total
        : throw new ArgumentOutOfRangeException(nameof(Total), Total, "The total of purchases cannot be negative.");

    /// <summary>The average amount; <see langword="null"/> when there is no purchase.</summary>
    public decimal? Average => Count == 0 ? null : Total / Count;
}
