using CardPlatform.Authorization.Application.Abstractions.Ledger;
using CardPlatform.Authorization.Application.Abstractions.Messaging;
using CardPlatform.Authorization.Application.Abstractions.Persistence;
using CardPlatform.Authorization.Application.Cards;
using CardPlatform.Authorization.Domain.Authorizations;
using CardPlatform.Authorization.Domain.Cards;
using CardPlatform.Authorization.Domain.Common;
using CardPlatform.Authorization.Domain.Monetary;

namespace CardPlatform.Authorization.Application.Authorizations;

/// <summary>
/// Handles <see cref="AuthorizePurchase"/>. The checks run in this order and the first one that fails decides:
/// <list type="number">
/// <item>input validation (errors, nothing recorded): <c>invalid_card_id</c>, <c>invalid_merchant_account_id</c>,
/// <c>invalid_currency</c>, <c>invalid_amount</c> (missing), <c>too_many_decimal_places</c>, <c>invalid_amount</c>
/// (not positive);</item>
/// <item>the card exists (error <c>card_not_found</c>, nothing recorded);</item>
/// <item>the card is active (declined <c>card_blocked</c>) and the amount is in the card currency (declined
/// <c>currency_mismatch</c>), see <see cref="CardAuthorization.CheckCard"/>; the Ledger is not called;</item>
/// <item>the hold is placed in the Ledger with the authorization id as hold id: approved when it is placed, declined
/// <c>insufficient_funds</c> when the Ledger refuses it for that reason.</item>
/// </list>
/// Any other Ledger failure is returned as it is (e.g. <see cref="LedgerErrors.UnavailableCode"/>) and nothing is
/// recorded. The merchant account is only checked for format: the Ledger checks it when the authorization is captured.
/// </summary>
/// <remarks>
/// The authorization id is chosen once, before the Ledger is called, because it is also the hold id: it is
/// <see cref="AuthorizePurchase.AuthorizationId"/> when given, otherwise a new one. Placing a hold is idempotent by id
/// in the Ledger, so the client may retry it safely. If the hold was placed but the answer was lost
/// (<see cref="LedgerErrors.UnavailableCode"/>) or the save fails, the hold stays pending in the Ledger with no
/// authorization; a retry of the same purchase reuses it because the idempotency layer gives it the same id
/// (see <see cref="Idempotency.IdempotentAuthorizePurchaseHandler"/>, ADR 0008).
/// </remarks>
public sealed class AuthorizePurchaseHandler(
    ILedgerClient ledger,
    ICardRepository cards,
    IAuthorizationRepository authorizations,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ICommandHandler<AuthorizePurchase, AuthorizationDto>
{
    public async Task<Result<AuthorizationDto>> HandleAsync(
        AuthorizePurchase command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.CardId is not { } cardId || cardId == Guid.Empty)
        {
            return Result.Failure<AuthorizationDto>(AuthorizationErrors.InvalidCardId);
        }

        if (command.MerchantAccountId is not { } merchantAccountId || merchantAccountId == Guid.Empty)
        {
            return Result.Failure<AuthorizationDto>(AuthorizationErrors.InvalidMerchantAccountId);
        }

        Result<Money> amount = ParseAmount(command.Amount, command.Currency);
        if (amount.IsFailure)
        {
            return Result.Failure<AuthorizationDto>(amount.Error);
        }

        Card? card = await cards.GetByIdAsync(cardId, cancellationToken).ConfigureAwait(false);
        if (card is null)
        {
            return Result.Failure<AuthorizationDto>(CardErrors.CardNotFound(cardId));
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        Guid authorizationId = command.AuthorizationId ?? CardAuthorization.NewId(now);

        if (CardAuthorization.CheckCard(card, amount.Value) is { } reason)
        {
            return await SaveAsync(
                    CardAuthorization.Decline(authorizationId, card, merchantAccountId, amount.Value, reason, now),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        Result<LedgerHold> hold = await ledger
            .PlaceHoldAsync(
                authorizationId,
                card.LedgerAccountId,
                amount.Value.Amount,
                amount.Value.Currency.Code,
                cancellationToken)
            .ConfigureAwait(false);

        if (hold.IsSuccess)
        {
            return await SaveAsync(
                    CardAuthorization.Approve(authorizationId, card, merchantAccountId, amount.Value, now),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (IsInsufficientFunds(hold.Error))
        {
            return await SaveAsync(
                    CardAuthorization.Decline(
                        authorizationId,
                        card,
                        merchantAccountId,
                        amount.Value,
                        DeclineReason.InsufficientFunds,
                        now),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return Result.Failure<AuthorizationDto>(hold.Error);
    }

    private async Task<Result<AuthorizationDto>> SaveAsync(
        CardAuthorization authorization,
        CancellationToken cancellationToken)
    {
        authorizations.Add(authorization);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success(AuthorizationDto.From(authorization));
    }

    private static bool IsInsufficientFunds(Error error)
    {
        return error.Type == ErrorType.BusinessRule && error.Code == LedgerErrors.InsufficientFundsCode;
    }

    private static Result<Money> ParseAmount(decimal? amount, string? currencyCode)
    {
        Result<Currency> currency = Currency.Create(currencyCode);
        if (currency.IsFailure)
        {
            return Result.Failure<Money>(currency.Error);
        }

        if (amount is not { } value)
        {
            return Result.Failure<Money>(AuthorizationErrors.MissingAmount);
        }

        Result<Money> money = Money.Create(value, currency.Value);
        if (money.IsFailure)
        {
            return money;
        }

        return value > 0m ? money : Result.Failure<Money>(AuthorizationErrors.NonPositiveAmount(value));
    }
}
