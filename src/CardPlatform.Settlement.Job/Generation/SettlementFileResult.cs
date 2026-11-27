using CardPlatform.Settlement.Job.FileLayout;

namespace CardPlatform.Settlement.Job.Generation;

/// <summary>A settlement file that was written completely and moved to its final name.</summary>
/// <param name="Path">Full path of the file.</param>
/// <param name="Trailer">The trailer written: number of details and sum of their amounts.</param>
/// <param name="Parallelism">
/// The degree of parallelism the file was written with (<see cref="RunOptions.Parallelism"/>); 1 for a strategy that
/// reads on one connection only.
/// </param>
public sealed record SettlementFileResult(string Path, SettlementTrailer Trailer, int Parallelism = 1);
