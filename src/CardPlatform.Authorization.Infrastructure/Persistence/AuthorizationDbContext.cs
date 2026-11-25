using CardPlatform.Authorization.Application.Abstractions.Persistence;
using CardPlatform.Authorization.Domain.Authorizations;
using CardPlatform.Authorization.Domain.Cards;
using CardPlatform.Authorization.Infrastructure.Persistence.Configurations;
using CardPlatform.Authorization.Infrastructure.Persistence.Idempotency;
using CardPlatform.Messaging.Outbox;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace CardPlatform.Authorization.Infrastructure.Persistence;

/// <summary>
/// EF Core context of the Authorization. Mappings live in <c>IEntityTypeConfiguration</c> classes of this assembly,
/// except the outbox's, shared through <c>CardPlatform.Messaging</c>; the domain model has no EF attributes.
/// </summary>
public sealed class AuthorizationDbContext(DbContextOptions<AuthorizationDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<Card> Cards => Set<Card>();

    public DbSet<CardAuthorization> Authorizations => Set<CardAuthorization>();

    public DbSet<IdempotencyKeyRecord> IdempotencyKeys => Set<IdempotencyKeyRecord>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <remarks>
    /// When the save fails, EF Core rolls its transaction back and every tracked change is then discarded, so nothing that
    /// was meant to be saved with it (an authorization, its outbox event, an idempotency key completion) can be written
    /// by a later save in the same scope.
    /// </remarks>
    async Task IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SaveTrackedChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            ChangeTracker.Clear();
            throw;
        }
    }

    private async Task SaveTrackedChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException exception)
            when (exception.Entries.Any(static entry => entry.Entity is IdempotencyKeyRecord))
        {
            // The idempotency key is no longer claimed by this request's token (ADR 0008). EF Core rolled the
            // transaction back, so the authorization was not saved either.
            throw new IdempotencyClaimLostException(
                "The idempotency key was claimed by another request; nothing was saved.",
                exception);
        }
        catch (DbUpdateException exception) when (IsDuplicateAuthorization(exception))
        {
            // Another request with the same idempotency key (and so the same reserved id) saved the authorization first.
            throw new IdempotencyClaimLostException(
                "The authorization was saved by another request with the same idempotency key; nothing was saved.",
                exception);
        }
    }

    private static bool IsDuplicateAuthorization(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: CardAuthorizationConfiguration.PrimaryKeyName,
        };
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuthorizationDbContext).Assembly);
        modelBuilder.ApplyOutbox();
    }
}
