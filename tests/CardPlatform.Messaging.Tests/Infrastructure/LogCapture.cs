using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

namespace CardPlatform.Messaging.Tests.Infrastructure;

/// <summary>Keeps every log entry written through it, with its structured values, so tests can see what a service did.</summary>
public sealed class LogCapture : ILoggerProvider
{
    public ConcurrentQueue<LogEntry> Entries { get; } = new();

    /// <summary>The offsets the consumer reports having consumed (its per-message log, event id 111), in order.</summary>
    public long[] ConsumedOffsets()
    {
        return
        [
            .. Entries.Where(static entry => entry.EventId == 111 && entry.Values.ContainsKey("Offset"))
                .Select(static entry => Convert.ToInt64(entry.Values["Offset"], System.Globalization.CultureInfo.InvariantCulture)),
        ];
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new Logger(this, categoryName);
    }

    public void Dispose()
    {
    }

    public sealed record LogEntry(string Category, int EventId, LogLevel Level, string Message, IReadOnlyDictionary<string, object?> Values);

    private sealed class Logger(LogCapture owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Dictionary<string, object?> values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.ToDictionary(static pair => pair.Key, static pair => pair.Value)
                : [];
            owner.Entries.Enqueue(new LogEntry(category, eventId.Id, logLevel, formatter(state, exception), values));
        }
    }
}
