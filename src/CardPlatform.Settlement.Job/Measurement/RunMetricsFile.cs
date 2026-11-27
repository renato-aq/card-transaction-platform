using System.Text.Json;
using System.Text.Json.Serialization;

namespace CardPlatform.Settlement.Job.Measurement;

/// <summary>
/// The JSON file of <see cref="RunMetrics"/> asked for with <c>--metrics-output</c>. It exists only after a successful
/// run: an earlier file at the same path is deleted before the run starts (<see cref="DeleteStale"/>), and the new one is
/// written under a temporary name and moved into place only when complete (<see cref="WriteAsync"/>), so a failed run
/// leaves no metrics file, neither partial nor from an earlier run.
/// </summary>
public static class RunMetricsFile
{
    /// <summary>Deletes the file at <paramref name="path"/>, if any, so it cannot be taken for the result of this run.</summary>
    public static void DeleteStale(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        // File.Delete throws when the directory does not exist yet.
        string fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }

    /// <summary>
    /// Writes <paramref name="metrics"/> to <paramref name="path"/>, creating its directory if needed and replacing an
    /// existing file, and returns the full path.
    /// </summary>
    public static async Task<string> WriteAsync(string path, RunMetrics metrics, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(metrics);

        string finalPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(finalPath);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = $"{finalPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true);
            await using (stream.ConfigureAwait(false))
            {
                await JsonSerializer.SerializeAsync(stream, metrics, RunMetricsJsonContext.Default.RunMetrics, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, finalPath, overwrite: true);
            return finalPath;
        }
        catch
        {
            File.Delete(temporaryPath);
            throw;
        }
    }
}

/// <summary>Source-generated JSON serialization of <see cref="RunMetrics"/>.</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(RunMetrics))]
internal sealed partial class RunMetricsJsonContext : JsonSerializerContext;
