using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

namespace CardPlatform.Settlement.Job.Tests.Infrastructure;

/// <summary>A logger provider that keeps every log entry in memory, so tests can assert what the job logged.</summary>
internal sealed class CapturedLogs : ILoggerProvider
{
    private readonly ConcurrentQueue<CapturedLogEntry> _entries = new();

    public IReadOnlyList<CapturedLogEntry> Entries => [.. _entries];

    public IReadOnlyList<CapturedLogEntry> Errors => [.. _entries.Where(entry => entry.Level >= LogLevel.Error)];

    public ILogger CreateLogger(string categoryName)
    {
        return new CapturingLogger(categoryName, _entries);
    }

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<CapturedLogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel != LogLevel.None;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            entries.Enqueue(new CapturedLogEntry(category, logLevel, formatter(state, exception), exception));
        }
    }
}

/// <summary>One captured log entry, with its formatted message.</summary>
internal sealed record CapturedLogEntry(string Category, LogLevel Level, string Message, Exception? Exception);
