using CardPlatform.Messaging.Publishing;

using Confluent.Kafka;

using Microsoft.Extensions.Logging;

namespace CardPlatform.Messaging.Kafka;

/// <summary>
/// Owns the Kafka producer shared by the outbox publisher (<see cref="KafkaOutboxMessageProducer"/>) and the consumer's
/// dead-letter topic (<see cref="KafkaDeadLetterProducer"/>), and replaces it after a fatal error (ADR 0010). A fatal
/// error (e.g. an unrecoverable sequence or epoch error of the idempotent producer) leaves a producer unable to send
/// anything again; the send that hit it fails as any other and is retried by its caller with backoff, and the next send
/// goes through a new producer, built the same way (the Aspire integration's settings and telemetry). The rest of the
/// service, e.g. the synchronous purchase path, keeps running. Thread-safe.
/// </summary>
/// <remarks>
/// <para>
/// A producer is never disposed while a send uses it: Confluent.Kafka does not guard a produce on a disposed handle
/// (librdkafka aborts the process), and <c>Dispose</c> does not flush, so a send in flight on a disposed producer would
/// never complete. Each send therefore takes the current producer and counts itself in flight on it, under one lock,
/// and leaves in a <see langword="finally"/>. After a fatal error the producer is retired: it is no longer handed out,
/// the next send builds a new one, and it is disposed by whichever comes last of the fatal report (when no send is in
/// flight on it) or the end of its last in-flight send.
/// </para>
/// <para>
/// A send in flight on a broken producer still completes: librdkafka fails each queued message once its delivery
/// timeout (<c>message.timeout.ms</c>, <see cref="KafkaProducerDefaults.DefaultMessageTimeoutMs"/> unless configured)
/// expires, so a retired producer is disposed at the latest that long after its fatal error.
/// </para>
/// <para>
/// On <see cref="Dispose"/> (host shutdown, after the hosted services stopped) no new send is accepted (it throws
/// <see cref="ObjectDisposedException"/>, which the callers, already cancelled by then, treat as stopping), and every
/// producer, current or retired, is disposed as soon as no send is in flight on it: at once when idle, which is the
/// normal case at shutdown, or when its last send completes (cancelled with the host or failed by the delivery timeout).
/// Disposing a producer under a send would crash the process instead of letting the host stop cleanly.
/// </para>
/// </remarks>
public sealed class KafkaProducerProvider : IDisposable
{
    private readonly Func<IProducer<string, byte[]>> _createProducer;
    private readonly ILogger<KafkaProducerProvider> _logger;
    private readonly Lock _lock = new();
    private readonly HashSet<Lease> _retired = [];
    private Lease? _current;
    private bool _disposed;

    /// <param name="createProducer">Builds a new producer; called on first use and after each fatal error.</param>
    /// <param name="logger">Logs the replacement of a producer.</param>
    public KafkaProducerProvider(Func<IProducer<string, byte[]>> createProducer, ILogger<KafkaProducerProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(createProducer);
        ArgumentNullException.ThrowIfNull(logger);

        _createProducer = createProducer;
        _logger = logger;
    }

    /// <summary>
    /// The producer the next send would use, built on first use and again after <see cref="Reset"/>. For inspection
    /// only: it is not counted in flight, so sending with it could race a replacement; send with <see cref="ProduceAsync"/>.
    /// </summary>
    internal IProducer<string, byte[]> Current
    {
        get
        {
            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return (_current ??= new Lease(_createProducer())).Producer;
            }
        }
    }

    /// <summary>
    /// Sends <paramref name="message"/> to <paramref name="topic"/> with the current producer and completes when the
    /// broker acknowledged it, or throws (<see cref="ProduceException{TKey, TValue}"/>, <see cref="KafkaException"/>). On a
    /// fatal error the producer is retired (<see cref="Reset"/>) before the exception is rethrown, so a retry uses a new one.
    /// </summary>
    public async Task<DeliveryResult<string, byte[]>> ProduceAsync(
        string topic,
        Message<string, byte[]> message,
        CancellationToken cancellationToken)
    {
        Lease lease = Acquire();
        try
        {
            return await lease.Producer.ProduceAsync(topic, message, cancellationToken).ConfigureAwait(false);
        }
        catch (KafkaException exception) when (exception.Error.IsFatal)
        {
            Retire(lease, exception);
            throw;
        }
        finally
        {
            Release(lease);
        }
    }

    /// <summary>
    /// Retires <paramref name="failed"/> after the fatal <paramref name="error"/> and lets the next send build a new
    /// producer; does nothing when <paramref name="failed"/> was already replaced (e.g. by a concurrent send that hit
    /// the same error). The retired producer is disposed now if no send is in flight on it, or when the last one ends.
    /// </summary>
    internal void Reset(IProducer<string, byte[]> failed, KafkaException error)
    {
        Lease? lease;
        lock (_lock)
        {
            lease = _current is not null && ReferenceEquals(_current.Producer, failed) ? _current : null;
        }

        if (lease is not null)
        {
            Retire(lease, error);
        }
    }

    public void Dispose()
    {
        List<Lease> idle = [];
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_current is not null)
            {
                _retired.Add(_current);
                _current = null;
            }

            foreach (Lease lease in _retired)
            {
                if (lease.InFlight == 0)
                {
                    idle.Add(lease);
                }
            }

            _retired.ExceptWith(idle);
        }

        // The others are disposed by the end of their last in-flight send (Release).
        foreach (Lease lease in idle)
        {
            lease.Producer.Dispose();
        }
    }

    /// <summary>Takes the current producer, building one if needed, and counts a send in flight on it.</summary>
    private Lease Acquire()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Lease lease = _current ??= new Lease(_createProducer());
            lease.InFlight++;
            return lease;
        }
    }

    /// <summary>Ends a send on <paramref name="lease"/>; the last send on a retired producer disposes it.</summary>
    private void Release(Lease lease)
    {
        lock (_lock)
        {
            lease.InFlight--;
            if (lease.InFlight > 0 || !_retired.Remove(lease))
            {
                return;
            }
        }

        lease.Producer.Dispose();
    }

    /// <summary>
    /// Stops handing out <paramref name="lease"/> if it is still the current producer, and disposes it when no send is in
    /// flight on it; otherwise keeps it retired until its last send ends. Only the first report of a producer replaces it.
    /// </summary>
    private void Retire(Lease lease, KafkaException error)
    {
        bool disposeNow;
        string name;
        lock (_lock)
        {
            if (_disposed || !ReferenceEquals(_current, lease))
            {
                return;
            }

            // Read under the lock: once it is released, a retired producer may be disposed by its last send.
            name = lease.Producer.Name;
            _current = null;
            disposeNow = lease.InFlight == 0;
            if (!disposeNow)
            {
                _retired.Add(lease);
            }
        }

        if (disposeNow)
        {
            lease.Producer.Dispose();
        }

        OutboxLog.ProducerReplaced(_logger, error, name);
    }

    /// <summary>A producer and the number of sends in flight on it, guarded by the provider's lock.</summary>
    private sealed class Lease(IProducer<string, byte[]> producer)
    {
        public IProducer<string, byte[]> Producer { get; } = producer;

        public int InFlight { get; set; }
    }
}
