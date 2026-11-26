using CardPlatform.Authorization.Application.Abstractions.Ledger;
using CardPlatform.Authorization.Application.Abstractions.Messaging;
using CardPlatform.Authorization.Application.Abstractions.Persistence;
using CardPlatform.Authorization.Domain.Authorizations;
using CardPlatform.Authorization.Domain.Common;

namespace CardPlatform.Authorization.Application.Authorizations;

/// <summary>
/// Handles <see cref="VoidAuthorization"/>:
/// <list type="number">
/// <item>the authorization exists (<c>404</c> <see cref="AuthorizationErrors.AuthorizationNotFoundCode"/>);</item>
/// <item>it is approved (<c>422</c> <see cref="CardAuthorization.NotVoidableCode"/> for declined, captured or voided
/// authorizations); the Ledger is not called;</item>
/// <item>the hold (same id as the authorization) is voided in the Ledger, returning the money to the card; then the
/// authorization is marked voided and saved.</item>
/// </list>
/// Ledger failures are returned as they are and leave the authorization unchanged (e.g.
/// <see cref="LedgerErrors.UnavailableCode"/>), except <see cref="LedgerErrors.HoldNotPendingCode"/>, which is
/// reconciled (see <see cref="AuthorizationReconciliation"/>).
/// </summary>
public sealed class VoidAuthorizationHandler(
    ILedgerClient ledger,
    IAuthorizationRepository authorizations,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ICommandHandler<VoidAuthorization, AuthorizationDto>
{
    public async Task<Result<AuthorizationDto>> HandleAsync(
        VoidAuthorization command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        CardAuthorization? authorization = await authorizations
            .GetByIdAsync(command.AuthorizationId, cancellationToken)
            .ConfigureAwait(false);
        if (authorization is null)
        {
            return Result.Failure<AuthorizationDto>(AuthorizationErrors.AuthorizationNotFound(command.AuthorizationId));
        }

        Result voidable = authorization.EnsureVoidable();
        if (voidable.IsFailure)
        {
            return Result.Failure<AuthorizationDto>(voidable.Error);
        }

        Result<LedgerHold> voided = await ledger
            .VoidHoldAsync(authorization.Id, cancellationToken)
            .ConfigureAwait(false);

        if (voided.IsFailure && !AuthorizationReconciliation.IsHoldNotPending(voided.Error))
        {
            return Result.Failure<AuthorizationDto>(voided.Error);
        }

        return await new AuthorizationReconciliation(ledger, outbox, unitOfWork, timeProvider)
            .CompleteAsync(
                authorization,
                voided.IsSuccess ? null : voided.Error,
                expected: LedgerHoldStatus.Voided,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
