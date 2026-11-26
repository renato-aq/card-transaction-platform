namespace CardPlatform.Settlement.Job.FileLayout;

/// <summary>
/// A fixed-width field of a settlement record: where it starts in the record (zero-based) and how many characters it takes.
/// </summary>
/// <param name="Offset">Zero-based offset of the first character of the field within the record.</param>
/// <param name="Width">Number of characters the field takes.</param>
public readonly record struct SettlementField(int Offset, int Width)
{
    /// <summary>One-based position of the first character, as layouts are usually documented.</summary>
    public int Position => Offset + 1;

    /// <summary>Zero-based offset just past the last character of the field.</summary>
    public int End => Offset + Width;
}
