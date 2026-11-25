namespace CardPlatform.Contracts.Tests;

public class TopicsTests
{
    [Fact]
    public void Topic_names_are_the_agreed_ones()
    {
        Assert.Equal("authorizations", Topics.Authorizations);
        Assert.Equal("fraud-signals", Topics.FraudSignals);
    }

    [Theory]
    [InlineData(Topics.Authorizations, "authorizations.dlt")]
    [InlineData(Topics.FraudSignals, "fraud-signals.dlt")]
    public void Dead_letter_topic_appends_the_dlt_suffix(string topic, string expected)
    {
        Assert.Equal(expected, Topics.DeadLetterOf(topic));
    }
}
