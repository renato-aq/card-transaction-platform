using CardPlatform.Messaging;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

using Testcontainers.Kafka;

namespace CardPlatform.Authorization.Api.Tests.Infrastructure;

/// <summary>
/// <see cref="AuthorizationApiFactory"/> plus a Kafka container, with the outbox publisher and the consumer of
/// <c>fraud-signals</c> enabled as in the AppHost (connection string <c>kafka</c>). The API runs in Development, so it
/// also creates its topics on startup.
/// </summary>
public sealed class KafkaAuthorizationApiFactory : AuthorizationApiFactory
{
    /// <summary>Same image as the Kafka container started by the AppHost (Aspire 17).</summary>
    private const string KafkaImage = "confluentinc/confluent-local:8.4.0";

    private readonly KafkaContainer _kafka = new KafkaBuilder(KafkaImage).Build();

    public string KafkaBootstrapServers => _kafka.GetBootstrapAddress();

    /// <summary>Every log entry the API wrote, e.g. what the consumer of <c>fraud-signals</c> did with each event.</summary>
    public LogCapture Logs { get; } = new();

    public override async Task InitializeAsync()
    {
        await _kafka.StartAsync();
        await base.InitializeAsync();
    }

    protected override async ValueTask DisposeDependenciesAsync()
    {
        await _kafka.DisposeAsync();
    }

    protected override void ConfigureMessaging(IWebHostBuilder builder)
    {
        builder.UseSetting($"ConnectionStrings:{MessagingHostingExtensions.DefaultKafkaConnectionName}", KafkaBootstrapServers);
        builder.UseSetting("Messaging:Outbox:PollingInterval", "00:00:00.100");
        builder.UseSetting("Messaging:Consumer:RetryInitialDelay", "00:00:00.200");
        builder.UseSetting("Messaging:Consumer:RetryMaxDelay", "00:00:01");

        // A rule for this provider wins over the appsettings' Default: Information.
        builder.ConfigureLogging(logging => logging.AddProvider(Logs).AddFilter<LogCapture>(category: null, LogLevel.Debug));
    }
}

[CollectionDefinition(Name)]
public sealed class KafkaAuthorizationApiCollectionFixture : ICollectionFixture<KafkaAuthorizationApiFactory>
{
    public const string Name = "Authorization API with Kafka";
}
