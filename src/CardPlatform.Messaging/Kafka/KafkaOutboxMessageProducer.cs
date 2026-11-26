using System.Globalization;
using System.Text;

using CardPlatform.Contracts;
using CardPlatform.Messaging.Outbox;
using CardPlatform.Messaging.Publishing;

using Confluent.Kafka;

namespace CardPlatform.Messaging.Kafka;

/// <summary>
/// Publishes outbox rows with the Kafka producer of <see cref="KafkaProducerProvider"/>, built by the Aspire integration
/// (idempotent, <c>acks=all</c>, see <see cref="KafkaProducerDefaults"/>) and replaced after a fatal error: key = partition key (the card id), value = the envelope as JSON written by
/// <see cref="EventSerializer"/>, headers = event id and type (<see cref="OutboxHeaders"/>).
/// </summary>
public sealed class KafkaOutboxMessageProducer(KafkaProducerProvider producer) : IOutboxMessageProducer
{
    public async Task ProduceAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        // Re-serialized from the envelope: jsonb does not keep the text as written, and this checks the row is an envelope.
        EventEnvelope envelope = message.ReadEnvelope();

        Message<string, byte[]> kafkaMessage = new()
        {
            Key = message.PartitionKey,
            Value = EventSerializer.SerializeToUtf8Bytes(envelope),
            Headers = new Headers
            {
                { OutboxHeaders.EventId, Encoding.UTF8.GetBytes(envelope.EventId.ToString("D", CultureInfo.InvariantCulture)) },
                { OutboxHeaders.EventType, Encoding.UTF8.GetBytes(envelope.Type) },
            },
        };

        // Completes when the broker acknowledged the message (all in-sync replicas), or throws ProduceException.
        await producer.ProduceAsync(message.Topic, kafkaMessage, cancellationToken).ConfigureAwait(false);
    }
}
