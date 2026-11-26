using CardPlatform.Authorization.Application.Abstractions.Messaging;
using CardPlatform.Authorization.Application.Cards;
using CardPlatform.Authorization.Domain.Cards;
using CardPlatform.Authorization.Domain.Common;
using CardPlatform.Authorization.Infrastructure.Consuming;
using CardPlatform.Contracts;
using CardPlatform.Contracts.Fraud;
using CardPlatform.Messaging.Consuming;

using Microsoft.Extensions.Logging;

namespace CardPlatform.Authorization.Infrastructure.Tests.Consuming;

/// <summary>What the inbound adapter of <see cref="FraudSuspected"/> does with each outcome of <see cref="BlockCard"/>.</summary>
public sealed class FraudSuspectedHandlerTests
{
    private static readonly Guid CardId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AuthorizationId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset DetectedAt = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);

    private static readonly FraudSuspected Suspected =
        new(CardId, AuthorizationId, "velocity", "3 approved purchases of the card within 60 s (threshold: 3).", DetectedAt);

    private static readonly EventEnvelope Envelope = EventEnvelope.Create(Suspected, Guid.CreateVersion7(), DetectedAt);

    private readonly RecordingLogger _logger = new();

    [Fact]
    public async Task Blocks_the_suspected_card_and_logs_the_suspicion()
    {
        FakeBlockCard blockCard = new(Result.Success(new CardDto(CardId, Guid.NewGuid(), "BRL", CardStatus.Blocked)));

        await new FraudSuspectedHandler(blockCard, _logger).HandleAsync(Suspected, Envelope, CancellationToken.None);

        Assert.Equal(CardId, Assert.Single(blockCard.Commands).CardId);
        (LogLevel level, string message) = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains(CardId.ToString(), message, StringComparison.Ordinal);
        Assert.Contains(AuthorizationId.ToString(), message, StringComparison.Ordinal);
        Assert.Contains("velocity", message, StringComparison.Ordinal);
        Assert.Contains(Suspected.Reason, message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_card_is_logged_and_the_event_confirmed()
    {
        FakeBlockCard blockCard = new(Result.Failure<CardDto>(CardErrors.CardNotFound(CardId)));

        // Returning normally lets the consumer commit the processed mark and the offset.
        await new FraudSuspectedHandler(blockCard, _logger).HandleAsync(Suspected, Envelope, CancellationToken.None);

        (LogLevel level, string message) = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains("unknown card", message, StringComparison.Ordinal);
        Assert.Contains(CardId.ToString(), message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Any_other_failure_rejects_the_event()
    {
        FakeBlockCard blockCard = new(Result.Failure<CardDto>(Error.BusinessRule("unexpected", "Unexpected failure.")));

        await Assert.ThrowsAsync<IntegrationEventRejectedException>(() =>
            new FraudSuspectedHandler(blockCard, _logger).HandleAsync(Suspected, Envelope, CancellationToken.None));
    }

    private sealed class FakeBlockCard(Result<CardDto> result) : ICommandHandler<BlockCard, CardDto>
    {
        public List<BlockCard> Commands { get; } = [];

        public Task<Result<CardDto>> HandleAsync(BlockCard command, CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            return Task.FromResult(result);
        }
    }

    private sealed class RecordingLogger : ILogger<FraudSuspectedHandler>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

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
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
