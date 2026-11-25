using Microsoft.EntityFrameworkCore;

namespace CardPlatform.Messaging.Consuming;

public static class ProcessedMessageModelBuilderExtensions
{
    /// <summary>
    /// Maps <see cref="ProcessedMessage"/> to <c>processed_messages</c> in the context being built, so the consumer marks
    /// each event as processed in the same transaction as the changes its handler saves with that context.
    /// </summary>
    public static ModelBuilder ApplyProcessedMessages(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        return modelBuilder.ApplyConfiguration(new ProcessedMessageConfiguration());
    }
}
