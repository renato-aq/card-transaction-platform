using CardPlatform.Messaging.Publishing;

namespace CardPlatform.Messaging.Tests.Publishing;

public sealed class OutboxRetryDelayTests
{
    private static readonly OutboxPublisherOptions Options = new()
    {
        RetryInitialDelay = TimeSpan.FromSeconds(1),
        RetryMaxDelay = TimeSpan.FromSeconds(30),
    };

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(5, 16)]
    [InlineData(6, 30)]
    [InlineData(1000, 30)]
    public void Delay_doubles_after_each_failure_up_to_the_maximum(int consecutiveFailures, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), OutboxRetryDelay.For(consecutiveFailures, Options));
    }

    [Fact]
    public void Delay_requires_at_least_one_failure()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OutboxRetryDelay.For(0, Options));
    }
}
