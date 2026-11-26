using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

namespace CardPlatform.Authorization.Api.Tests.Infrastructure;

/// <summary>A log entry kept by <see cref="LogCapture"/>, with its structured values.</summary>
public sealed record CapturedLog(
    string Category,
    LogLevel Level,
    int EventId,
    IReadOnlyDictionary<string, object?> Values,
    Exception? Exception);

/// <summary>Keeps every log entry written through it, so tests can see what the API's background services did.</summary>
public sealed class LogCapture : ILoggerProvider
{
    private readonly ConcurrentQueue<CapturedLog> _entries = new();

    public IReadOnlyList<CapturedLog> Entries => [.. _entries];

    /// <summary>
    /// The entries of the categories starting with <paramref name="categoryPrefix"/>, with <paramref name="eventId"/>,
    /// whose value <c>EventId</c> is <paramref name="integrationEventId"/>.
    /// </summary>
    public CapturedLog[] OfEvent(string categoryPrefix, int eventId, Guid integrationEventId)
    {
        return
        [
            .. _entries.Where(entry =>
                entry.Category.StartsWith(categoryPrefix, StringComparison.Ordinal)
                && entry.EventId == eventId
                && entry.Values.TryGetValue("EventId", out object? value)
                && value is Guid id
                && id == integrationEventId),
        ];
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new Logger(this, categoryName);
    }

    public void Dispose()
    {
    }

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
            owner._entries.Enqueue(new CapturedLog(category, logLevel, eventId.Id, values, exception));
        }
    }
}
