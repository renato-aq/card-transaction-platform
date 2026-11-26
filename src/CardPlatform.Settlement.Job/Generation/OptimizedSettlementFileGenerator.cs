using System.Data;
using System.Runtime.ExceptionServices;

using CardPlatform.Settlement.Job.FileLayout;
using CardPlatform.Settlement.Job.ReadModel;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CardPlatform.Settlement.Job.Generation;

/// <summary>
/// Version 2: streaming, optionally in parallel partitions. It reads the settlement entries page by page
/// (<see cref="SettlementPageReader"/>, keyset pagination, untracked projections) and writes them as they arrive, in
/// batches of <see cref="SettlementFileWriter.BatchLines"/> lines; the trailer is accumulated in the same pass. Memory
/// depends on the page and batch sizes and on <see cref="RunOptions.Parallelism"/>, not on the number of records, and the
/// file is the same as version 1's whatever the parallelism.
/// </summary>
/// <remarks>
/// <para>
/// With a parallelism of 1 the pages are read one after the other in one repeatable read transaction, so every page sees
/// the same snapshot of the ledger, as the single query of version 1 does.
/// </para>
/// <para>
/// With a parallelism of n &gt; 1 the merchant accounts, read in the order of the file, are split into at most n
/// contiguous ranges (<see cref="SettlementPartitions"/>), each read on a connection of its own, at the same time. Every
/// connection sees the same snapshot: the transaction that read the merchants exports its snapshot, reads the first
/// partition and stays open until the file is complete; the others import that snapshot (<see cref="SettlementSnapshot"/>).
/// So the run holds n connections at most. Each partition writes its details to an intermediate file next to the
/// settlement file, numbered from 1 within the partition (<see cref="SettlementPartitionWriter"/>). The partitions are
/// joined in the order of their ranges as soon as each one and those before it are complete: its lines are appended to the
/// settlement file and renumbered after the details already there (<see cref="SettlementFileWriter.AppendPartitionAsync"/>),
/// so the numbering is continuous, and its intermediate file is deleted.
/// </para>
/// <para>
/// The first partition to fail cancels the others; the run then fails with that partition's error, and no settlement file
/// nor intermediate file is left. Intermediate files are deleted whatever the outcome, except when the process itself is
/// killed, as the temporary settlement file is.
/// </para>
/// </remarks>
public sealed class OptimizedSettlementFileGenerator(
    LedgerReadDbContext dbContext,
    SettlementPageReader reader,
    IServiceScopeFactory scopeFactory)
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
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Parallelism);

        SettlementFileResult result = options.Parallelism == 1
            ? await GenerateSequentiallyAsync(options, cancellationToken).ConfigureAwait(false)
            : await GenerateInPartitionsAsync(options, cancellationToken).ConfigureAwait(false);
        return result with { Parallelism = options.Parallelism };
    }

    private async Task<SettlementFileResult> GenerateSequentiallyAsync(RunOptions options, CancellationToken cancellationToken)
    {
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
                // completed, so a failure to end it still leaves no settlement file. The commit does not take the token:
                // a commit cancelled midway is logged by EF as a transaction error, while ending a read-only transaction
                // is harmless and quick; a cancelled run is still stopped before the file is completed.
                await snapshot.CommitAsync(CancellationToken.None).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return await writer.CompleteAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<SettlementFileResult> GenerateInPartitionsAsync(RunOptions options, CancellationToken cancellationToken)
    {
        SettlementHeader header = new(options.Date, options.Currency);

        // The exporting transaction: it reads the merchants and the first partition, and stays open until every
        // partition is read, so the others can import its snapshot.
        IDbContextTransaction snapshot = await dbContext.Database
            .BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken)
            .ConfigureAwait(false);
        await using (snapshot.ConfigureAwait(false))
        {
            SettlementFileWriter writer = await SettlementFileWriter.CreateAsync(options.OutputDirectory, header, cancellationToken)
                .ConfigureAwait(false);
            await using (writer.ConfigureAwait(false))
            {
                string snapshotId = await SettlementSnapshot.ExportAsync(dbContext, cancellationToken).ConfigureAwait(false);

                List<Guid> merchants = [];
                await foreach (Guid merchant in reader.MerchantsAsync(options, cancellationToken).ConfigureAwait(false))
                {
                    merchants.Add(merchant);
                }

                IReadOnlyList<SettlementPartition> partitions = SettlementPartitions.Split(merchants.Count, options.Parallelism);
                PartitionedRun run = new(
                    reader,
                    scopeFactory,
                    options,
                    header,
                    merchants,
                    snapshotId,
                    IntermediatePathPrefix(options, header));
                await run.WriteAsync(writer, partitions, cancellationToken).ConfigureAwait(false);

                // As in the sequential version, the snapshot is released before the file is completed, without the token,
                // and a cancelled run is stopped right after.
                await snapshot.CommitAsync(CancellationToken.None).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return await writer.CompleteAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    // The intermediate files are next to the settlement file, named after it and after the run, so two runs never share
    // one and every one is easy to tell apart: settlement-<date>-<currency>.txt.<run>.part<index>.tmp.
    private static string IntermediatePathPrefix(RunOptions options, SettlementHeader header)
    {
        string finalPath = Path.Combine(Path.GetFullPath(options.OutputDirectory), SettlementFileWriter.FileName(header));
        return $"{finalPath}.{Guid.NewGuid():N}.part";
    }

    // Reads the merchants of one partition, in their order, into its intermediate file and returns its trailer.
    private static async Task<SettlementTrailer> WritePartitionAsync(
        SettlementPageReader partitionReader,
        RunOptions options,
        SettlementHeader header,
        IEnumerable<Guid> merchants,
        string path,
        CancellationToken cancellationToken)
    {
        SettlementPartitionWriter partition = SettlementPartitionWriter.Create(path, header);
        await using (partition.ConfigureAwait(false))
        {
            List<SettlementRecord> batch = new(SettlementFileWriter.BatchLines);
            foreach (Guid merchant in merchants)
            {
                await foreach (SettlementRow row in partitionReader.ReadMerchantAsync(options, merchant, cancellationToken)
                    .ConfigureAwait(false))
                {
                    batch.Add(row.Record);
                    if (batch.Count == SettlementFileWriter.BatchLines)
                    {
                        await partition.WriteBatchAsync(batch, cancellationToken).ConfigureAwait(false);
                        batch.Clear();
                    }
                }
            }

            await partition.WriteBatchAsync(batch, cancellationToken).ConfigureAwait(false);
            await partition.CompleteAsync(cancellationToken).ConfigureAwait(false);
            return partition.Trailer;
        }
    }

    // One parallel run: starts a task per partition and joins their files into the settlement file in partition order.
    private sealed class PartitionedRun(
        SettlementPageReader exportingReader,
        IServiceScopeFactory scopeFactory,
        RunOptions options,
        SettlementHeader header,
        List<Guid> merchants,
        string snapshotId,
        string intermediatePathPrefix)
    {
        private Exception? _firstFailure;

        public async Task WriteAsync(
            SettlementFileWriter writer,
            IReadOnlyList<SettlementPartition> partitions,
            CancellationToken cancellationToken)
        {
            string[] paths = [.. partitions.Select(partition => $"{intermediatePathPrefix}{partition.Index}.tmp")];
            Task<SettlementTrailer>[] tasks = new Task<SettlementTrailer>[partitions.Count];

            // Cancelled by the first partition that fails, so the others stop, or by the caller.
            using CancellationTokenSource failure = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            try
            {
                for (int index = 0; index < partitions.Count; index++)
                {
                    SettlementPartition partition = partitions[index];
                    string path = paths[index];
                    tasks[index] = Task.Run(() => ReadPartitionAsync(partition, path, failure), CancellationToken.None);
                }

                // In the order of the file: a partition is appended once it and every partition before it are complete,
                // while the partitions after it are still being read.
                for (int index = 0; index < partitions.Count; index++)
                {
                    SettlementTrailer trailer = await tasks[index].ConfigureAwait(false);
                    await writer.AppendPartitionAsync(paths[index], trailer, failure.Token).ConfigureAwait(false);
                    File.Delete(paths[index]);
                }
            }
            catch (Exception exception)
            {
                Interlocked.CompareExchange(ref _firstFailure, exception, null);
                await failure.CancelAsync().ConfigureAwait(false);

                // Every partition is stopped, its file closed, before the files are deleted.
                await Task.WhenAll(tasks.Where(task => task is not null).Cast<Task>())
                    .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

                // The partition that failed first gives the error of the run, not the cancellation it caused in the
                // others; a cancellation by the caller is rethrown as such.
                ExceptionDispatchInfo.Throw(_firstFailure ?? exception);
            }
            finally
            {
                foreach (string path in paths)
                {
                    File.Delete(path);
                }
            }
        }

        private async Task<SettlementTrailer> ReadPartitionAsync(
            SettlementPartition partition,
            string path,
            CancellationTokenSource failure)
        {
            CancellationToken cancellationToken = failure.Token;
            IEnumerable<Guid> partitionMerchants = merchants.Skip(partition.FirstMerchant).Take(partition.MerchantCount);
            try
            {
                // The first partition is read by the exporting transaction itself.
                if (partition.Index == 0)
                {
                    return await WritePartitionAsync(exportingReader, options, header, partitionMerchants, path, cancellationToken)
                        .ConfigureAwait(false);
                }

                AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                await using (scope.ConfigureAwait(false))
                {
                    LedgerReadDbContext partitionContext = scope.ServiceProvider.GetRequiredService<LedgerReadDbContext>();
                    IDbContextTransaction transaction = await partitionContext.Database
                        .BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken)
                        .ConfigureAwait(false);
                    await using (transaction.ConfigureAwait(false))
                    {
                        await SettlementSnapshot.ImportAsync(partitionContext, snapshotId, cancellationToken).ConfigureAwait(false);
                        SettlementTrailer trailer = await WritePartitionAsync(
                            scope.ServiceProvider.GetRequiredService<SettlementPageReader>(),
                            options,
                            header,
                            partitionMerchants,
                            path,
                            cancellationToken).ConfigureAwait(false);
                        // Not with the token: when a sibling fails right after this partition was read, a commit
                        // cancelled midway is logged by EF as a transaction error. The transaction is read-only, so
                        // ending it is harmless and quick; the cancelled run is stopped by the join.
                        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
                        return trailer;
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !failure.IsCancellationRequested)
            {
                // Not a consequence of a cancellation: this partition failed, and stops the others.
                Interlocked.CompareExchange(ref _firstFailure, exception, null);
                await failure.CancelAsync().ConfigureAwait(false);
                throw;
            }
        }
    }
}
