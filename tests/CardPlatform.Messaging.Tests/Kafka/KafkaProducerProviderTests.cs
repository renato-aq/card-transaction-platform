using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

using CardPlatform.Messaging.Kafka;
using CardPlatform.Messaging.Tests.Infrastructure;

using Confluent.Kafka;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CardPlatform.Messaging.Tests.Kafka;

/// <summary>The producer shared by the outbox publisher and the dead-letter topic, replaced after a fatal error.</summary>
public sealed class KafkaProducerProviderTests
{
    private static readonly Message<string, byte[]> Message = new() { Key = "card", Value = [1, 2, 3] };

    [Fact]
    public async Task A_fatal_error_with_no_other_send_in_flight_disposes_the_producer_and_the_next_send_uses_a_new_one()
    {
        (IProducer<string, byte[]> broken, FakeProducer brokenFake) = FakeProducer.Fatal("broken");
        (IProducer<string, byte[]> working, FakeProducer workingFake) = FakeProducer.Working("working");
        Queue<IProducer<string, byte[]>> producers = new([broken, working]);
        LogCapture logs = new();
        using KafkaProducerProvider provider = new(producers.Dequeue, CreateLogger(logs));

        KafkaException failure = await Assert.ThrowsAnyAsync<KafkaException>(
            () => provider.ProduceAsync("topic", Message, CancellationToken.None));
        Assert.True(failure.Error.IsFatal);
        Assert.Equal(1, brokenFake.DisposeCount);

        await provider.ProduceAsync("topic", Message, CancellationToken.None);

        Assert.Single(brokenFake.Sent);
        Assert.Same(Message, Assert.Single(workingFake.Sent));
        Assert.Same(working, provider.Current);
        Assert.False(workingFake.Disposed);
        Assert.Equal(0, brokenFake.UsedAfterDispose);
        Assert.Single(logs.Entries, static entry => entry.EventId == ProducerReplacedLogId);
    }

    [Fact]
    public void Reporting_an_idle_producer_disposes_it_at_once()
    {
        (IProducer<string, byte[]> producer, FakeProducer fake) = FakeProducer.Working("idle");
        (IProducer<string, byte[]> next, _) = FakeProducer.Working("next");
        Queue<IProducer<string, byte[]>> producers = new([producer, next]);
        using KafkaProducerProvider provider = new(producers.Dequeue, NullLogger<KafkaProducerProvider>.Instance);
        Assert.Same(producer, provider.Current);

        provider.Reset(producer, FakeProducer.FatalError());

        Assert.Equal(1, fake.DisposeCount);
        Assert.Same(next, provider.Current);
    }

    [Fact]
    public async Task A_send_in_flight_when_another_caller_replaces_the_producer_keeps_it_until_the_send_completes()
    {
        TaskCompletionSource<DeliveryResult<string, byte[]>> delivery = NewDelivery();
        (IProducer<string, byte[]> old, FakeProducer oldFake) = FakeProducer.Create("old", _ => delivery.Task);
        (IProducer<string, byte[]> replacement, FakeProducer replacementFake) = FakeProducer.Working("replacement");
        Queue<IProducer<string, byte[]>> producers = new([old, replacement]);
        using KafkaProducerProvider provider = new(producers.Dequeue, NullLogger<KafkaProducerProvider>.Instance);

        // E.g. the outbox publisher's send, still waiting for its delivery report.
        Task<DeliveryResult<string, byte[]>> inFlight = provider.ProduceAsync("topic", Message, CancellationToken.None);
        Assert.Single(oldFake.Sent);

        // E.g. the dead-letter path hit a fatal error on the same producer meanwhile.
        provider.Reset(old, FakeProducer.FatalError());

        Assert.False(oldFake.Disposed);
        await provider.ProduceAsync("topic", Message, CancellationToken.None);
        Assert.Single(replacementFake.Sent);
        Assert.Single(oldFake.Sent);
        Assert.False(oldFake.Disposed);

        delivery.SetResult(new DeliveryResult<string, byte[]> { Message = Message });
        await inFlight;

        Assert.Equal(1, oldFake.DisposeCount);
        Assert.Equal(0, oldFake.UsedAfterDispose);
        Assert.False(replacementFake.Disposed);
        Assert.Same(replacement, provider.Current);
    }

    [Fact]
    public async Task Concurrent_fatal_errors_on_the_same_producer_replace_it_once_and_dispose_it_once()
    {
        TaskCompletionSource<DeliveryResult<string, byte[]>> delivery = NewDelivery();
        (IProducer<string, byte[]> broken, FakeProducer brokenFake) = FakeProducer.Create("broken", _ => delivery.Task);
        (IProducer<string, byte[]> working, _) = FakeProducer.Working("working");
        int created = 0;
        Queue<IProducer<string, byte[]>> producers = new([broken, working]);
        LogCapture logs = new();
        using KafkaProducerProvider provider = new(
            () =>
            {
                Interlocked.Increment(ref created);
                return producers.Dequeue();
            },
            CreateLogger(logs));

        // The publisher and the dead-letter path both have a send in flight when the producer fails.
        Task<DeliveryResult<string, byte[]>>[] sends =
            [.. Enumerable.Range(0, 8).Select(_ => provider.ProduceAsync("topic", Message, CancellationToken.None))];
        Assert.Equal(8, brokenFake.Sent.Count);
        delivery.SetException(FakeProducer.FatalError());
        foreach (Task<DeliveryResult<string, byte[]>> send in sends)
        {
            await Assert.ThrowsAnyAsync<KafkaException>(() => send);
        }

        // Late reports of the same producer, e.g. from threads that saw the error a bit later.
        Parallel.For(0, 8, _ => provider.Reset(broken, FakeProducer.FatalError()));

        Assert.Equal(1, brokenFake.DisposeCount);
        Assert.Equal(0, brokenFake.UsedAfterDispose);
        Assert.Same(working, provider.Current);
        Assert.Equal(2, created);
        Assert.Single(logs.Entries, static entry => entry.EventId == ProducerReplacedLogId);
    }

    [Fact]
    public async Task Disposing_the_provider_disposes_the_current_producer()
    {
        (IProducer<string, byte[]> producer, FakeProducer fake) = FakeProducer.Working("only");
        KafkaProducerProvider provider = new(() => producer, NullLogger<KafkaProducerProvider>.Instance);
        _ = provider.Current;

        provider.Dispose();

        Assert.Equal(1, fake.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => provider.Current);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => provider.ProduceAsync("topic", Message, CancellationToken.None));
    }

    [Fact]
    public async Task Disposing_the_provider_disposes_the_current_and_the_retired_producers_once_their_sends_complete()
    {
        TaskCompletionSource<DeliveryResult<string, byte[]>> retiredDelivery = NewDelivery();
        TaskCompletionSource<DeliveryResult<string, byte[]>> currentDelivery = NewDelivery();
        (IProducer<string, byte[]> retired, FakeProducer retiredFake) = FakeProducer.Create("retired", _ => retiredDelivery.Task);
        (IProducer<string, byte[]> current, FakeProducer currentFake) = FakeProducer.Create("current", _ => currentDelivery.Task);
        Queue<IProducer<string, byte[]>> producers = new([retired, current]);
        KafkaProducerProvider provider = new(producers.Dequeue, NullLogger<KafkaProducerProvider>.Instance);

        Task<DeliveryResult<string, byte[]>> onRetired = provider.ProduceAsync("topic", Message, CancellationToken.None);
        provider.Reset(retired, FakeProducer.FatalError());
        Task<DeliveryResult<string, byte[]>> onCurrent = provider.ProduceAsync("topic", Message, CancellationToken.None);

        provider.Dispose();

        // Neither is disposed under its in-flight send; each is once the send completes (here cancelled, as at shutdown).
        Assert.False(retiredFake.Disposed);
        Assert.False(currentFake.Disposed);
        retiredDelivery.SetCanceled();
        currentDelivery.SetCanceled();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => onRetired);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => onCurrent);

        Assert.Equal(1, retiredFake.DisposeCount);
        Assert.Equal(1, currentFake.DisposeCount);
        Assert.Equal(0, retiredFake.UsedAfterDispose + currentFake.UsedAfterDispose);
    }

    [Fact]
    public async Task Disposing_the_provider_disposes_the_idle_current_producer_at_once()
    {
        TaskCompletionSource<DeliveryResult<string, byte[]>> delivery = NewDelivery();
        (IProducer<string, byte[]> retired, FakeProducer retiredFake) = FakeProducer.Create("retired", _ => delivery.Task);
        (IProducer<string, byte[]> current, FakeProducer currentFake) = FakeProducer.Working("current");
        Queue<IProducer<string, byte[]>> producers = new([retired, current]);
        KafkaProducerProvider provider = new(producers.Dequeue, NullLogger<KafkaProducerProvider>.Instance);
        Task<DeliveryResult<string, byte[]>> onRetired = provider.ProduceAsync("topic", Message, CancellationToken.None);
        provider.Reset(retired, FakeProducer.FatalError());
        _ = provider.Current;

        provider.Dispose();

        Assert.Equal(1, currentFake.DisposeCount);
        Assert.False(retiredFake.Disposed);
        delivery.SetResult(new DeliveryResult<string, byte[]> { Message = Message });
        await onRetired;
        Assert.Equal(1, retiredFake.DisposeCount);
    }

    [Fact]
    public async Task Under_concurrent_sends_fatal_errors_and_resets_no_producer_is_used_or_disposed_under_a_send()
    {
        const int Senders = 8;
        const int SendsPerSender = 10_000;
        ConcurrentQueue<(FakeProducer Fake, StrongBox<int> DisposedUnderSend)> created = new();
        ConcurrentQueue<Exception> unexpected = new();
        int fatalErrors = 0;
        KafkaProducerProvider provider = new(
            () =>
            {
                StrongBox<int> disposedUnderSend = new();
                FakeProducer? fake = null;
                (IProducer<string, byte[]> producer, fake) = FakeProducer.Create(
                    $"producer-{created.Count}",
                    async message =>
                    {
                        // Completes at once, after a yield or after a short delay, so sends overlap retirements.
                        int roll = Random.Shared.Next(100);
                        if (roll < 2)
                        {
                            await Task.Delay(1);
                        }
                        else if (roll < 60)
                        {
                            await Task.Yield();
                        }

                        if (fake!.Disposed)
                        {
                            Interlocked.Increment(ref disposedUnderSend.Value);
                        }

                        return roll >= 99
                            ? throw FakeProducer.FatalError()
                            : new DeliveryResult<string, byte[]> { Message = message };
                    });
                created.Enqueue((fake, disposedUnderSend));
                return producer;
            },
            NullLogger<KafkaProducerProvider>.Instance);

        using CancellationTokenSource sendersDone = new();
        Task resets = Task.Run(async () =>
        {
            // Another caller reporting the current producer as broken, at random moments.
            while (!sendersDone.IsCancellationRequested)
            {
                provider.Reset(provider.Current, FakeProducer.FatalError());
                Thread.SpinWait(Random.Shared.Next(5_000));
                await Task.Yield();
            }
        });
        Task[] senders =
        [
            .. Enumerable.Range(0, Senders).Select(_ => Task.Run(async () =>
            {
                for (int i = 0; i < SendsPerSender; i++)
                {
                    try
                    {
                        await provider.ProduceAsync("topic", Message, CancellationToken.None);
                    }
                    catch (KafkaException exception) when (exception.Error.IsFatal)
                    {
                        Interlocked.Increment(ref fatalErrors);
                    }
#pragma warning disable CA1031 // Any other failure, e.g. a fake producer used after its disposal, fails the test below.
                    catch (Exception exception)
#pragma warning restore CA1031
                    {
                        unexpected.Enqueue(exception);
                    }
                }
            })),
        ];
        await Task.WhenAll(senders);
        await sendersDone.CancelAsync();
        await resets;

        Assert.Empty(unexpected);
        Assert.True(fatalErrors > 0);
        Assert.True(created.Count > Senders);
        Assert.All(created, static entry => Assert.Equal(0, entry.Fake.UsedAfterDispose));
        Assert.All(created, static entry => Assert.Equal(0, entry.DisposedUnderSend.Value));

        // Every send completed: each retired producer was disposed exactly once, and the current one not at all.
        IProducer<string, byte[]> current = provider.Current;
        FakeProducer currentFake = (FakeProducer)current;
        Assert.Equal(0, currentFake.DisposeCount);
        Assert.All(
            created.Where(entry => !ReferenceEquals(entry.Fake, currentFake)),
            static entry => Assert.Equal(1, entry.Fake.DisposeCount));

        provider.Dispose();
        Assert.Equal(1, currentFake.DisposeCount);
    }

    [Fact]
    public void The_registered_provider_builds_each_producer_through_the_aspire_integration()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings { DisableDefaults = true, EnvironmentName = Environments.Production });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            // Building a producer does not connect to the broker.
            [$"ConnectionStrings:{MessagingHostingExtensions.DefaultKafkaConnectionName}"] = "127.0.0.1:1",
        });
        builder.AddOutboxPublisher<TestOutboxDbContext>();
        using IHost host = builder.Build();

        KafkaProducerProvider provider = host.Services.GetRequiredService<KafkaProducerProvider>();
        IProducer<string, byte[]> first = provider.Current;
        provider.Reset(first, FakeProducer.FatalError());
        IProducer<string, byte[]> second = provider.Current;

        Assert.NotSame(first, second);
        // The integration's producer type, which carries its tracing; logging and metrics are set on its builder.
        Assert.StartsWith("InstrumentedProducer", second.GetType().Name, StringComparison.Ordinal);
        Assert.Null(host.Services.GetService<IProducer<string, byte[]>>());
    }

    private const int ProducerReplacedLogId = 5;

    private static TaskCompletionSource<DeliveryResult<string, byte[]>> NewDelivery()
    {
        return new TaskCompletionSource<DeliveryResult<string, byte[]>>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static ILogger<KafkaProducerProvider> CreateLogger(LogCapture logs)
    {
        return LoggerFactory.Create(logging => logging.AddProvider(logs)).CreateLogger<KafkaProducerProvider>();
    }
}
