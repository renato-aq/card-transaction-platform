using System.Globalization;

using CardPlatform.Settlement.Job.FileLayout;

namespace CardPlatform.Settlement.Job.Generation;

/// <summary>
/// Writes one settlement file, shared by every <see cref="ISettlementFileGenerator"/>: the header on creation, one detail
/// per record given to <see cref="WriteAsync"/> or <see cref="WriteBatchAsync"/>, or per line of a partition given to
/// <see cref="AppendPartitionAsync"/> (numbered from 1 in the order written, and added to the trailer), and the trailer
/// on <see cref="CompleteAsync"/>.
/// </summary>
/// <remarks>
/// The file is written under a temporary name in the output directory and moved to its final name
/// (<see cref="FileName"/>) only by <see cref="CompleteAsync"/>, once it is complete and flushed to disk, replacing a
/// file of an earlier run. Disposing a writer that was not completed, e.g. after a failure, deletes the temporary file,
/// so a failed run never leaves a final file, partial or not. A line is written to the file only after it was formatted
/// successfully: the formatter may leave partial bytes in the buffer when it throws.
/// </remarks>
public sealed class SettlementFileWriter : IAsyncDisposable
{
    /// <summary>
    /// Detail lines formatted into one buffer and written to the file at once by <see cref="WriteBatchAsync"/>: about
    /// 59 KiB, so a batch is a single write that bypasses the stream's own small buffer.
    /// </summary>
    public const int BatchLines = 512;

    private readonly SettlementLineFormatter _formatter;

    private readonly FileStream _stream;

    private readonly byte[] _line = new byte[SettlementFileLayout.LineLength];

    // Allocated by the first batch, so a writer that only writes line by line never holds it.
    private byte[]? _batch;

    private readonly string _temporaryPath;

    private readonly string _finalPath;

    private SettlementTrailer _trailer = SettlementTrailer.Empty;

    private bool _completed;

    private bool _disposed;

    private SettlementFileWriter(SettlementLineFormatter formatter, FileStream stream, string temporaryPath, string finalPath)
    {
        _formatter = formatter;
        _stream = stream;
        _temporaryPath = temporaryPath;
        _finalPath = finalPath;
    }

    /// <summary>The trailer of the details written so far.</summary>
    public SettlementTrailer Trailer => _trailer;

    /// <summary>Name of the settlement file of <paramref name="header"/>: <c>settlement-&lt;yyyyMMdd&gt;-&lt;currency&gt;.txt</c>.</summary>
    public static string FileName(SettlementHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"settlement-{header.SettlementDate:yyyyMMdd}-{header.Currency}.txt");
    }

    /// <summary>
    /// Creates <paramref name="outputDirectory"/> if needed and a temporary file in it, and writes the header.
    /// </summary>
    public static async Task<SettlementFileWriter> CreateAsync(
        string outputDirectory,
        SettlementHeader header,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(header);

        // Validates the header (currency) before anything is created.
        SettlementLineFormatter formatter = new(header);

        string directory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(directory);
        string finalPath = Path.Combine(directory, FileName(header));
        string temporaryPath = $"{finalPath}.{Guid.NewGuid():N}.tmp";

        FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true);
        SettlementFileWriter writer = new(formatter, stream, temporaryPath, finalPath);
        try
        {
            int length = formatter.WriteHeader(writer._line);
            await stream.WriteAsync(writer._line.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
            return writer;
        }
        catch
        {
            await writer.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Writes the next detail line and adds it to the trailer.</summary>
    public async Task WriteAsync(SettlementRecord record, CancellationToken cancellationToken)
    {
        ThrowIfFinished();

        SettlementDetail detail = ToDetail(_trailer.DetailCount + 1, record);
        int length = _formatter.WriteDetail(detail, _line);
        await _stream.WriteAsync(_line.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
        _trailer = _trailer.Add(detail);
    }

    /// <summary>
    /// Writes the next detail lines, one per record and in their order, and adds them to the trailer: the same lines as
    /// one <see cref="WriteAsync"/> per record, formatted into a buffer of up to <see cref="BatchLines"/> lines that is
    /// written to the file at once.
    /// </summary>
    public async Task WriteBatchAsync(IReadOnlyList<SettlementRecord> records, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);
        ThrowIfFinished();

        _batch ??= new byte[BatchLines * SettlementFileLayout.LineLength];
        for (int start = 0; start < records.Count; start += BatchLines)
        {
            int count = Math.Min(BatchLines, records.Count - start);

            // Only a buffer formatted completely is written: the formatter may leave partial bytes when it throws.
            SettlementTrailer trailer = FormatDetails(_formatter, _trailer, records, start, count, _batch);
            await _stream.WriteAsync(_batch.AsMemory(0, count * SettlementFileLayout.LineLength), cancellationToken)
                .ConfigureAwait(false);
            _trailer = trailer;
        }
    }

    /// <summary>
    /// Appends the detail lines of a partition (<see cref="SettlementPartitionWriter"/>), in the order they are in its file,
    /// numbered after the details written so far, and adds <paramref name="partitionTrailer"/>, the partition's own trailer,
    /// to the trailer. The lines are copied as they were formatted; only their sequence number is rewritten.
    /// </summary>
    /// <remarks>
    /// This is where the details of a partition get their place in the file: a partition numbers its lines from 1, as it
    /// cannot know how many details come before it until the partitions before it are complete.
    /// </remarks>
    public async Task AppendPartitionAsync(
        string partitionPath,
        SettlementTrailer partitionTrailer,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(partitionPath);
        ThrowIfFinished();

        _batch ??= new byte[BatchLines * SettlementFileLayout.LineLength];
        FileStream source = new(partitionPath, FileMode.Open, FileAccess.Read, FileShare.None, bufferSize: 1, useAsync: true);
        await using (source.ConfigureAwait(false))
        {
            if (source.Length != partitionTrailer.DetailCount * SettlementFileLayout.LineLength)
            {
                throw new InvalidOperationException(
                    $"The partition file {partitionPath} does not hold the {partitionTrailer.DetailCount} details of its trailer.");
            }

            long sequenceNumber = _trailer.DetailCount;
            int read;
            while ((read = await source.ReadAtLeastAsync(_batch, _batch.Length, throwOnEndOfStream: false, cancellationToken)
                .ConfigureAwait(false)) > 0)
            {
                for (int offset = 0; offset < read; offset += SettlementFileLayout.LineLength)
                {
                    SettlementLineFormatter.RenumberDetail(++sequenceNumber, _batch.AsSpan(offset, SettlementFileLayout.LineLength));
                }

                await _stream.WriteAsync(_batch.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
        }

        _trailer = _trailer.Add(partitionTrailer);
    }

    /// <summary>
    /// Writes the trailer, flushes the file to disk and moves it to its final name, replacing an existing file.
    /// </summary>
    public async Task<SettlementFileResult> CompleteAsync(CancellationToken cancellationToken)
    {
        ThrowIfFinished();

        int length = _formatter.WriteTrailer(_trailer, _line);
        await _stream.WriteAsync(_line.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        _stream.Flush(flushToDisk: true);
        await _stream.DisposeAsync().ConfigureAwait(false);

        File.Move(_temporaryPath, _finalPath, overwrite: true);
        _completed = true;
        return new SettlementFileResult(_finalPath, _trailer);
    }

    /// <summary>Closes the file; if it was not completed, deletes the temporary file.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _stream.DisposeAsync().ConfigureAwait(false);
        if (!_completed)
        {
            File.Delete(_temporaryPath);
        }
    }

    private static SettlementDetail ToDetail(long sequenceNumber, SettlementRecord record)
    {
        return new SettlementDetail(
            sequenceNumber,
            record.TransactionId,
            record.MerchantAccountId,
            record.Amount,
            record.TransactedAt);
    }

    /// <summary>
    /// Formats <c>records[start..start + count)</c> into consecutive detail lines of <paramref name="buffer"/>, numbered
    /// after the details counted by <paramref name="trailer"/>, and returns <paramref name="trailer"/> including them.
    /// </summary>
    internal static SettlementTrailer FormatDetails(
        SettlementLineFormatter formatter,
        SettlementTrailer trailer,
        IReadOnlyList<SettlementRecord> records,
        int start,
        int count,
        Span<byte> buffer)
    {
        for (int index = 0; index < count; index++)
        {
            SettlementDetail detail = ToDetail(trailer.DetailCount + 1, records[start + index]);
            formatter.WriteDetail(detail, buffer[(index * SettlementFileLayout.LineLength)..]);
            trailer = trailer.Add(detail);
        }

        return trailer;
    }

    private void ThrowIfFinished()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed)
        {
            throw new InvalidOperationException("The settlement file is already complete.");
        }
    }
}
