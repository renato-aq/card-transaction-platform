using System.Globalization;

using CardPlatform.Settlement.Job.FileLayout;

namespace CardPlatform.Settlement.Job.Generation;

/// <summary>
/// Writes one settlement file, shared by every <see cref="ISettlementFileGenerator"/>: the header on creation, one detail
/// per <see cref="WriteAsync"/> (numbered from 1 in the order written, and added to the trailer), and the trailer on
/// <see cref="CompleteAsync"/>.
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
    private readonly SettlementLineFormatter _formatter;

    private readonly FileStream _stream;

    private readonly byte[] _line = new byte[SettlementFileLayout.LineLength];

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

        SettlementDetail detail = new(
            _trailer.DetailCount + 1,
            record.TransactionId,
            record.MerchantAccountId,
            record.Amount,
            record.TransactedAt);
        int length = _formatter.WriteDetail(detail, _line);
        await _stream.WriteAsync(_line.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
        _trailer = _trailer.Add(detail);
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

    private void ThrowIfFinished()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed)
        {
            throw new InvalidOperationException("The settlement file is already complete.");
        }
    }
}
