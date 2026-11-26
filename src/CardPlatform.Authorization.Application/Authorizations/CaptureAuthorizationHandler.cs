using CardPlatform.Authorization.Application.Abstractions.Ledger;
using CardPlatform.Authorization.Application.Abstractions.Messaging;
using CardPlatform.Authorization.Application.Abstractions.Persistence;
using CardPlatform.Authorization.Domain.Authorizations;
using CardPlatform.Authorization.Domain.Common;

namespace CardPlatform.Authorization.Application.Authorizations;

/// <summary>
/// Handles <see cref="CaptureAuthorization"/>:
/// <list type="number">
/// <item>the authorization exists (<c>404</c> <see cref="AuthorizationErrors.AuthorizationNotFoundCode"/>);</item>
/// <item>it is approved (<c>422</c> <see cref="CardAuthorization.NotCapturableCode"/> for declined, captured or voided
/// authorizations); the Ledger is not called;</item>
/// <item>the hold (same id as the authorization) is captured in the Ledger to the authorization's merchant account;
/// then the authorization is marked captured and saved.</item>
/// </list>
/// Ledger failures are returned as they are and leave the authorization unchanged (e.g.
/// <see cref="LedgerErrors.UnavailableCode"/>, <see cref="LedgerErrors.NotAMerchantAccountCode"/>,
/// <see cref="LedgerErrors.AccountNotFoundCode"/>, <see cref="LedgerErrors.CurrencyMismatchCode"/>), except
/// <see cref="LedgerErrors.HoldNotPendingCode"/>, which is reconciled (see <see cref="AuthorizationReconciliation"/>).
/// </summary>
public sealed class CaptureAuthorizationHandler(
    ILedgerClient ledger,
    IAuthorizationRepository authorizations,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ICommandHandler<CaptureAuthorization, AuthorizationDto>
{
    public async Task<Result<AuthorizationDto>> HandleAsync(
        CaptureAuthorization command,
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

        Result capturable = authorization.EnsureCapturable();
        if (capturable.IsFailure)
        {
            return Result.Failure<AuthorizationDto>(capturable.Error);
        }

        Result<LedgerHold> captured = await ledger
            .CaptureHoldAsync(authorization.Id, authorization.MerchantAccountId, cancellationToken)
            .ConfigureAwait(false);

        if (captured.IsFailure && !AuthorizationReconciliation.IsHoldNotPending(captured.Error))
        {
            return Result.Failure<AuthorizationDto>(captured.Error);
        }

        return await new AuthorizationReconciliation(ledger, outbox, unitOfWork, timeProvider)
            .CompleteAsync(
                authorization,
                captured.IsSuccess ? null : captured.Error,
                expected: LedgerHoldStatus.Captured,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
