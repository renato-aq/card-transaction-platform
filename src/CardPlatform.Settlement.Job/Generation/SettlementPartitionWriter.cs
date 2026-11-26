using CardPlatform.Settlement.Job.FileLayout;

namespace CardPlatform.Settlement.Job.Generation;

/// <summary>
/// Writes the detail lines of one partition (<see cref="SettlementPartitions"/>) to an intermediate file, formatted exactly
/// as <see cref="SettlementFileWriter"/> formats them but numbered from 1 within the partition, and accumulates the
/// partition's trailer. Its file holds detail lines only, no header and no trailer, and is later appended to the
/// settlement file by <see cref="SettlementFileWriter.AppendPartitionAsync"/>, which gives each line its number in the
/// file.
/// </summary>
/// <remarks>
/// The writer never deletes its file: the caller owns the path and removes it whether the run succeeds or fails. As in
/// <see cref="SettlementFileWriter"/>, only lines formatted completely are written, in batches of up to
/// <see cref="SettlementFileWriter.BatchLines"/> lines.
/// </remarks>
public sealed class SettlementPartitionWriter : IAsyncDisposable
{
    private readonly SettlementLineFormatter _formatter;

    private readonly FileStream _stream;

    private readonly byte[] _batch = new byte[SettlementFileWriter.BatchLines * SettlementFileLayout.LineLength];

    private SettlementTrailer _trailer = SettlementTrailer.Empty;

    private SettlementPartitionWriter(SettlementLineFormatter formatter, FileStream stream)
    {
        _formatter = formatter;
        _stream = stream;
    }

    /// <summary>The trailer of the details written so far: how many and the sum of their amounts.</summary>
    public SettlementTrailer Trailer => _trailer;

    /// <summary>Creates the intermediate file at <paramref name="path"/>, which must not exist yet.</summary>
    public static SettlementPartitionWriter Create(string path, SettlementHeader header)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(header);

        SettlementLineFormatter formatter = new(header);
        FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 1, useAsync: true);
        return new SettlementPartitionWriter(formatter, stream);
    }

    /// <summary>Writes the next detail lines, one per record and in their order, and adds them to the trailer.</summary>
    public async Task WriteBatchAsync(IReadOnlyList<SettlementRecord> records, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);

        for (int start = 0; start < records.Count; start += SettlementFileWriter.BatchLines)
        {
            int count = Math.Min(SettlementFileWriter.BatchLines, records.Count - start);
            SettlementTrailer trailer = SettlementFileWriter.FormatDetails(_formatter, _trailer, records, start, count, _batch);
            await _stream.WriteAsync(_batch.AsMemory(0, count * SettlementFileLayout.LineLength), cancellationToken)
                .ConfigureAwait(false);
            _trailer = trailer;
        }
    }

    /// <summary>Flushes and closes the file, which is then ready to be appended.</summary>
    public async Task CompleteAsync(CancellationToken cancellationToken)
    {
        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        await _stream.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Closes the file, if still open; the file itself is left for the caller to delete.</summary>
    public ValueTask DisposeAsync()
    {
        return _stream.DisposeAsync();
    }
}
