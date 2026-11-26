using CardPlatform.Authorization.Application.Abstractions.Ledger;
using CardPlatform.Authorization.Application.Abstractions.Persistence;
using CardPlatform.Authorization.Domain.Authorizations;
using CardPlatform.Authorization.Domain.Common;

namespace CardPlatform.Authorization.Application.Authorizations;

/// <summary>
/// Completes an approved authorization after its capture or void in the Ledger, aligning the local status with the
/// hold's, which is the source of truth (ADR 0007).
/// <para>
/// Capturing and voiding are not idempotent in the Ledger: a hold that is no longer pending answers
/// <see cref="LedgerErrors.HoldNotPendingCode"/>. That happens when an earlier attempt completed the hold but its reply
/// was lost (the client saw <see cref="LedgerErrors.UnavailableCode"/> and the authorization stayed approved), or when
/// a concurrent request completed it first. The hold is then read and the authorization follows it:
/// </para>
/// <list type="bullet">
/// <item>the hold is in the expected state (captured for a capture, voided for a void): the authorization is completed
/// the same way and the operation succeeds, as if this attempt had done it;</item>
/// <item>the hold is in the other final state: the authorization is completed that way and the operation fails with
/// <see cref="CardAuthorization.NotCapturableCode"/> / <see cref="CardAuthorization.NotVoidableCode"/>, exactly as if
/// the local status had been up to date;</item>
/// <item>the hold cannot be read (e.g. <see cref="LedgerErrors.UnavailableCode"/>): that error is returned and the
/// authorization is unchanged, so a later attempt reconciles it;</item>
/// <item>the hold is still pending (the Ledger contradicts itself): the original
/// <see cref="LedgerErrors.HoldNotPendingCode"/> error is returned and the authorization is unchanged.</item>
/// </list>
/// </summary>
/// <remarks>
/// The <c>authorizations</c> table has no concurrency token. Concurrent captures and voids of the same authorization
/// are serialized by the Ledger (the hold row has one, ADR 0007): only one completes the hold, and every other request
/// gets <see cref="LedgerErrors.HoldNotPendingCode"/> and reconciles to the hold's final state. Every save therefore
/// writes the same final status, which can never change again, so a last-writer-wins update converges; at worst
/// <see cref="CardAuthorization.CompletedAt"/> holds the time of the last request that recorded it.
/// <para>
/// Every completion is saved with the event of the status the authorization ends up in (<c>AuthorizationCaptured</c> or
/// <c>AuthorizationVoided</c>, also when it is the other final state), added to the outbox before the same save.
/// Concurrent writers may both add it; the events share a deterministic id (see <see cref="AuthorizationEvents"/>).
/// </para>
/// </remarks>
internal sealed class AuthorizationReconciliation(
    ILedgerClient ledger,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    /// <summary>Whether a Ledger capture or void failed because the hold was already captured or voided.</summary>
    public static bool IsHoldNotPending(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Type == ErrorType.BusinessRule && error.Code == LedgerErrors.HoldNotPendingCode;
    }

    /// <summary>
    /// Completes <paramref name="authorization"/> as <paramref name="expected"/> (<see cref="LedgerHoldStatus.Captured"/>
    /// or <see cref="LedgerHoldStatus.Voided"/>) and saves it. <paramref name="holdNotPending"/> is
    /// <see langword="null"/> when the Ledger completed the hold in this attempt; otherwise it is the Ledger's
    /// <see cref="LedgerErrors.HoldNotPendingCode"/> error, and the hold is read to reconcile.
    /// </summary>
    public async Task<Result<AuthorizationDto>> CompleteAsync(
        CardAuthorization authorization,
        Error? holdNotPending,
        LedgerHoldStatus expected,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        LedgerHoldStatus actual = expected;
        if (holdNotPending is not null)
        {
            Result<LedgerHold> hold = await ledger.GetHoldAsync(authorization.Id, cancellationToken).ConfigureAwait(false);
            if (hold.IsFailure)
            {
                return Result.Failure<AuthorizationDto>(hold.Error);
            }

            if (hold.Value.Status == LedgerHoldStatus.Pending)
            {
                return Result.Failure<AuthorizationDto>(holdNotPending);
            }

            actual = hold.Value.Status;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        Result completed = actual switch
        {
            LedgerHoldStatus.Captured => authorization.Capture(now),
            LedgerHoldStatus.Voided => authorization.Void(now),
            _ => throw new ArgumentOutOfRangeException(nameof(expected), actual, "The hold is not completed."),
        };

        if (completed.IsFailure)
        {
            // The authorization was checked before the Ledger was called; a failure here is a programming error.
            throw new InvalidOperationException(completed.Error.Message);
        }

        outbox.AddEventOf(authorization);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (actual == expected)
        {
            return Result.Success(AuthorizationDto.From(authorization));
        }

        Result refused = expected == LedgerHoldStatus.Captured
            ? authorization.EnsureCapturable()
            : authorization.EnsureVoidable();
        return Result.Failure<AuthorizationDto>(refused.Error);
    }
}
