using System.Data;

using CardPlatform.Settlement.Job.FileLayout;
using CardPlatform.Settlement.Job.ReadModel;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CardPlatform.Settlement.Job.Generation;

/// <summary>
/// Version 2: streaming. It reads the settlement entries page by page (<see cref="SettlementPageReader"/>, keyset
/// pagination, untracked projections) and writes them as they arrive, in batches of
/// <see cref="SettlementFileWriter.BatchLines"/> lines; the trailer is accumulated by the writer in the same pass. Memory
/// depends on the page and batch sizes, not on the number of records, and the file is the same as version 1's.
/// </summary>
/// <remarks>
/// The pages are read in one repeatable read transaction, so every page sees the same snapshot of the ledger, as the
/// single query of version 1 does.
/// </remarks>
public sealed class OptimizedSettlementFileGenerator(LedgerReadDbContext dbContext, SettlementPageReader reader)
    : ISettlementFileGenerator
{
    /// <summary>Name of this strategy.</summary>
    public const string StrategyName = "optimized";

    /// <inheritdoc/>
    public string Name => StrategyName;

    /// <inheritdoc/>
    public async Task<SettlementFileResult> GenerateAsync(RunOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        IDbContextTransaction snapshot = await dbContext.Database
            .BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken)
            .ConfigureAwait(false);
        await using (snapshot.ConfigureAwait(false))
        {
            SettlementFileWriter writer = await SettlementFileWriter.CreateAsync(
                options.OutputDirectory,
                new SettlementHeader(options.Date, options.Currency),
                cancellationToken).ConfigureAwait(false);
            await using (writer.ConfigureAwait(false))
            {
                List<SettlementRecord> batch = new(SettlementFileWriter.BatchLines);
                await foreach (SettlementRow row in reader.ReadAsync(options, cancellationToken).ConfigureAwait(false))
                {
                    batch.Add(row.Record);
                    if (batch.Count == SettlementFileWriter.BatchLines)
                    {
                        await writer.WriteBatchAsync(batch, cancellationToken).ConfigureAwait(false);
                        batch.Clear();
                    }
                }

                await writer.WriteBatchAsync(batch, cancellationToken).ConfigureAwait(false);

                // Nothing was changed: ending the transaction only releases the snapshot. It ends before the file is
                // completed, so a failure to end it still leaves no settlement file.
                await snapshot.CommitAsync(cancellationToken).ConfigureAwait(false);
                return await writer.CompleteAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
