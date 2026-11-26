using CardPlatform.Settlement.Job.FileLayout;
using CardPlatform.Settlement.Job.ReadModel;

using Microsoft.EntityFrameworkCore;

namespace CardPlatform.Settlement.Job.Generation;

/// <summary>
/// Version 1, the baseline to measure and then optimize: deliberately naive. It loads every settlement entry of the
/// day, with its transaction, into memory as tracked entities, builds the list of records, and only then writes the
/// file. Memory grows with the number of records.
/// </summary>
public sealed class NaiveSettlementFileGenerator(LedgerReadDbContext dbContext) : ISettlementFileGenerator
{
    /// <summary>Name of this strategy.</summary>
    public const string StrategyName = "naive";

    /// <inheritdoc/>
    public string Name => StrategyName;

    /// <inheritdoc/>
    public async Task<SettlementFileResult> GenerateAsync(RunOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<EntryRecord> entries = await SettlementQuery.Entries(dbContext, options)
            .Include(entry => entry.Transaction)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<SettlementRecord> records = [];
        foreach (EntryRecord entry in entries)
        {
            records.Add(new SettlementRecord(entry.AccountId, entry.TransactionId, entry.Amount, entry.Transaction.CreatedAt));
        }

        SettlementFileWriter writer = await SettlementFileWriter.CreateAsync(
            options.OutputDirectory,
            new SettlementHeader(options.Date, options.Currency),
            cancellationToken).ConfigureAwait(false);
        await using (writer.ConfigureAwait(false))
        {
            foreach (SettlementRecord record in records)
            {
                await writer.WriteAsync(record, cancellationToken).ConfigureAwait(false);
            }

            return await writer.CompleteAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
