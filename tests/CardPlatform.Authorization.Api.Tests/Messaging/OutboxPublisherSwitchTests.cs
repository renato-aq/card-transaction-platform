using CardPlatform.Authorization.Api.Tests.Infrastructure;
using CardPlatform.Authorization.Infrastructure.Persistence;
using CardPlatform.Messaging.Consuming;
using CardPlatform.Messaging.Kafka;
using CardPlatform.Messaging.Publishing;

using Confluent.Kafka;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CardPlatform.Authorization.Api.Tests.Messaging;

/// <summary>
/// <c>Messaging:Outbox:Enabled = false</c> and <c>Messaging:Consumer:Enabled = false</c> (set by
/// <see cref="AuthorizationApiFactory"/>) register no publisher, no consumer, no topic initializer and no Kafka client,
/// so the API runs without a broker.
/// </summary>
[Collection(AuthorizationApiCollectionFixture.Name)]
public sealed class OutboxPublisherSwitchTests(AuthorizationApiFactory factory)
{
    [Fact]
    public void Disabled_outbox_publisher_registers_nothing_that_needs_kafka()
    {
        IHostedService[] hostedServices = [.. factory.Services.GetServices<IHostedService>()];

        Assert.DoesNotContain(hostedServices, service => service is OutboxPublisher<AuthorizationDbContext>);
        Assert.DoesNotContain(hostedServices, service => service is KafkaTopicInitializer);
        Assert.Null(factory.Services.GetService<KafkaProducerProvider>());
        Assert.Null(factory.Services.GetService<IProducer<string, byte[]>>());
    }

    [Fact]
    public void Disabled_consumer_registers_nothing_that_needs_kafka()
    {
        // Messaging:Consumer:Enabled = false (also set by the factory): no consumer of fraud-signals, no Kafka consumer.
        Assert.DoesNotContain(
            factory.Services.GetServices<IHostedService>(),
            service => service is IntegrationEventConsumer<AuthorizationDbContext>);
        Assert.Null(factory.Services.GetService<IConsumer<string, byte[]>>());
        Assert.Null(factory.Services.GetService<IntegrationEventProcessor<AuthorizationDbContext>>());
    }
}
