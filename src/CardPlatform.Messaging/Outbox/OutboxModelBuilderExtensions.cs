using Microsoft.EntityFrameworkCore;

namespace CardPlatform.Messaging.Outbox;

public static class OutboxModelBuilderExtensions
{
    /// <summary>
    /// Maps <see cref="OutboxMessage"/> to <c>outbox_messages</c> in the context being built, so the events are saved by
    /// the same <c>SaveChanges</c>, in the same transaction, as the changes they describe.
    /// </summary>
    public static ModelBuilder ApplyOutbox(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        return modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
    }
}
