namespace CardPlatform.Settlement.Job.FileLayout;

/// <summary>
/// Trailer of a settlement file: how many details it has and the sum of their amounts.
/// Built by accumulation, so it can be computed while the details are streamed: start from <see cref="Empty"/>
/// and <see cref="Add"/> each detail.
/// </summary>
/// <param name="DetailCount">Number of detail records.</param>
/// <param name="TotalAmount">Sum of the detail amounts, in major units of the file currency.</param>
public readonly record struct SettlementTrailer(long DetailCount, decimal TotalAmount)
{
    public static SettlementTrailer Empty => default;

    /// <summary>Returns the trailer with <paramref name="detail"/> counted and its amount added.</summary>
    public SettlementTrailer Add(SettlementDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);
        return new SettlementTrailer(DetailCount + 1, TotalAmount + detail.Amount);
    }

    /// <summary>Returns the trailer with the details of <paramref name="other"/> counted and their amounts added.</summary>
    public SettlementTrailer Add(SettlementTrailer other)
    {
        return new SettlementTrailer(DetailCount + other.DetailCount, TotalAmount + other.TotalAmount);
    }
}
