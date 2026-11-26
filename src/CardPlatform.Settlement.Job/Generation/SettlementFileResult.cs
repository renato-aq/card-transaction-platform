using CardPlatform.Settlement.Job.FileLayout;

namespace CardPlatform.Settlement.Job.Generation;

/// <summary>A settlement file that was written completely and moved to its final name.</summary>
/// <param name="Path">Full path of the file.</param>
/// <param name="Trailer">The trailer written: number of details and sum of their amounts.</param>
public sealed record SettlementFileResult(string Path, SettlementTrailer Trailer);
