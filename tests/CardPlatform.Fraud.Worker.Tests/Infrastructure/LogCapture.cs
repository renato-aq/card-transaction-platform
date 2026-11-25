using System.Collections.Concurrent;
using System.Globalization;

using Microsoft.Extensions.Logging;

namespace CardPlatform.Fraud.Worker.Tests.Infrastructure;

/// <summary>Keeps every log entry written through it, with its structured values, so tests can see what the worker did.</summary>
public sealed class LogCapture : ILoggerProvider
{
    /// <summary>Event id of the consumer's log of each message it consumed (<c>ConsumerLog.MessageConsumed</c>).</summary>
    private const int MessageConsumedEventId = 111;

    private readonly ConcurrentQueue<(int EventId, IReadOnlyDictionary<string, object?> Values)> _entries = new();

    /// <summary>The offsets the consumer reports having consumed, in order.</summary>
    public long[] ConsumedOffsets()
    {
        return
        [
            .. _entries.Where(static entry => entry.EventId == MessageConsumedEventId && entry.Values.ContainsKey("Offset"))
                .Select(static entry => Convert.ToInt64(entry.Values["Offset"], CultureInfo.InvariantCulture)),
        ];
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new Logger(this);
    }

    public void Dispose()
    {
    }

    private sealed class Logger(LogCapture owner) : ILogger
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
            owner._entries.Enqueue((eventId.Id, values));
        }
    }
}
