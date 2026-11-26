using System.Collections.Concurrent;
using System.Reflection;

using Confluent.Kafka;

namespace CardPlatform.Messaging.Tests.Infrastructure;

/// <summary>
/// An <see cref="IProducer{TKey, TValue}"/> without a broker: <c>ProduceAsync(topic, message)</c> runs a given function
/// and records the message, <c>Dispose</c> is recorded, and any other member throws <see cref="NotSupportedException"/>.
/// Using it after <c>Dispose</c>, which would abort the process with a real producer, is recorded and throws.
/// </summary>
public class FakeProducer : DispatchProxy
{
    private Func<Message<string, byte[]>, Task<DeliveryResult<string, byte[]>>> _produce = null!;
    private string _name = string.Empty;
    private int _disposed;
    private int _usedAfterDispose;

    /// <summary>The messages passed to <c>ProduceAsync</c>, in order, including those that failed.</summary>
    public ConcurrentQueue<Message<string, byte[]>> Sent { get; } = new();

    /// <summary>How many times the producer was disposed.</summary>
    public int DisposeCount => Volatile.Read(ref _disposed);

    public bool Disposed => DisposeCount > 0;

    /// <summary>How many times a member was called after <c>Dispose</c> (a native crash with a real producer).</summary>
    public int UsedAfterDispose => Volatile.Read(ref _usedAfterDispose);

    /// <summary>A producer whose sends all succeed.</summary>
    public static (IProducer<string, byte[]> Producer, FakeProducer Fake) Working(string name)
    {
        return Create(name, static message => Task.FromResult(new DeliveryResult<string, byte[]> { Message = message }));
    }

    /// <summary>A producer whose sends all fail with a fatal error, as an idempotent producer after e.g. an epoch error.</summary>
    public static (IProducer<string, byte[]> Producer, FakeProducer Fake) Fatal(string name)
    {
        return Create(name, static _ => Task.FromException<DeliveryResult<string, byte[]>>(FatalError()));
    }

    public static KafkaException FatalError()
    {
        return new KafkaException(new Error(ErrorCode.Local_Fatal, "Fatal idempotent producer error (test).", isFatal: true));
    }

    /// <summary>A producer whose sends run <paramref name="produce"/>, e.g. to hold a send in flight.</summary>
    public static (IProducer<string, byte[]> Producer, FakeProducer Fake) Create(
        string name,
        Func<Message<string, byte[]>, Task<DeliveryResult<string, byte[]>>> produce)
    {
        IProducer<string, byte[]> producer = Create<IProducer<string, byte[]>, FakeProducer>();
        FakeProducer fake = (FakeProducer)producer;
        fake._name = name;
        fake._produce = produce;
        return (producer, fake);
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (Disposed && targetMethod?.Name != nameof(IDisposable.Dispose))
        {
            Interlocked.Increment(ref _usedAfterDispose);
            throw new InvalidOperationException($"{targetMethod?.Name} was called on the disposed fake producer {_name}.");
        }

        switch (targetMethod?.Name)
        {
            case nameof(IProducer<string, byte[]>.ProduceAsync) when args is [string, Message<string, byte[]> message, ..]:
                Sent.Enqueue(message);
                return _produce(message);
            case "get_" + nameof(IClient.Name):
                return _name;
            case nameof(IDisposable.Dispose):
                Interlocked.Increment(ref _disposed);
                return null;
            default:
                throw new NotSupportedException($"{targetMethod?.Name} is not supported by the fake producer.");
        }
    }
}
