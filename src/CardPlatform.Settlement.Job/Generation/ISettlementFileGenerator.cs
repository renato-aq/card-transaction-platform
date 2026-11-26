namespace CardPlatform.Settlement.Job.Generation;

/// <summary>
/// A strategy to produce the settlement file of a date and a currency. Every strategy selects the records with
/// <see cref="SettlementQuery"/>, in its order, and writes them with a <see cref="SettlementFileWriter"/>, so all of
/// them produce the same file; they differ only in how the records are read and handed to the writer.
/// </summary>
public interface ISettlementFileGenerator
{
    /// <summary>Writes the settlement file described by <paramref name="options"/> and returns it.</summary>
    Task<SettlementFileResult> GenerateAsync(RunOptions options, CancellationToken cancellationToken);
}
