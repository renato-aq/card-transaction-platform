using CardPlatform.Authorization.Application.Authorizations;
using CardPlatform.Authorization.Domain.Authorizations;
using CardPlatform.Authorization.Domain.Cards;
using CardPlatform.Authorization.Domain.Monetary;
using CardPlatform.Contracts.Authorizations;

using ContractDeclineReasons = CardPlatform.Contracts.Authorizations.DeclineReasons;
using DeclineReasons = CardPlatform.Authorization.Application.Authorizations.DeclineReasons;

namespace CardPlatform.Authorization.Application.Tests.Authorizations;

public class AuthorizationEventsTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);

    private readonly Card _card = Card.Create(Guid.NewGuid(), Currency.Create("BRL").Value, CreatedAt.AddDays(-1));
    private readonly Guid _merchantAccountId = Guid.NewGuid();

    [Fact]
    public void Event_id_is_the_same_for_the_same_authorization_and_event_type()
    {
        Guid authorizationId = Guid.CreateVersion7();

        Guid id = AuthorizationEvents.EventIdOf(authorizationId, typeof(AuthorizationCaptured));

        Assert.Equal(id, AuthorizationEvents.EventIdOf(authorizationId, typeof(AuthorizationCaptured)));
        Assert.Equal(5, id.Version);
    }

    [Fact]
    public void Event_id_differs_by_event_type_and_by_authorization()
    {
        Guid authorizationId = Guid.CreateVersion7();
        Type[] types = [typeof(AuthorizationApproved), typeof(AuthorizationDeclined), typeof(AuthorizationCaptured), typeof(AuthorizationVoided)];

        Guid[] ids = [.. types.Select(type => AuthorizationEvents.EventIdOf(authorizationId, type))];

        Assert.Equal(types.Length, ids.Distinct().Count());
        Assert.NotEqual(
            AuthorizationEvents.EventIdOf(authorizationId, typeof(AuthorizationApproved)),
            AuthorizationEvents.EventIdOf(Guid.CreateVersion7(), typeof(AuthorizationApproved)));
    }

    [Fact]
    public void Event_follows_the_current_status_of_the_authorization()
    {
        CardAuthorization authorization = Approve();
        Assert.IsType<AuthorizationApproved>(AuthorizationEvents.From(authorization));

        Assert.True(authorization.Capture(CreatedAt.AddHours(1)).IsSuccess);

        AuthorizationCaptured captured = Assert.IsType<AuthorizationCaptured>(AuthorizationEvents.From(authorization));
        Assert.Equal(
            new AuthorizationCaptured(authorization.Id, _card.Id, _merchantAccountId, 30m, "BRL", CreatedAt, CreatedAt.AddHours(1)),
            captured);
    }

    [Fact]
    public void Declined_event_carries_the_reason_code()
    {
        CardAuthorization declined = CardAuthorization.Decline(
            Guid.CreateVersion7(), _card, _merchantAccountId, Money.Create(30m, "BRL").Value, DeclineReason.CurrencyMismatch, CreatedAt);

        AuthorizationDeclined @event = Assert.IsType<AuthorizationDeclined>(AuthorizationEvents.From(declined));

        Assert.Equal(ContractDeclineReasons.CurrencyMismatch, @event.Reason);
    }

    [Fact]
    public void Api_decline_codes_are_the_contract_codes()
    {
        Assert.Equal(ContractDeclineReasons.CardBlocked, DeclineReasons.ToCode(DeclineReason.CardBlocked));
        Assert.Equal(ContractDeclineReasons.CurrencyMismatch, DeclineReasons.ToCode(DeclineReason.CurrencyMismatch));
        Assert.Equal(ContractDeclineReasons.InsufficientFunds, DeclineReasons.ToCode(DeclineReason.InsufficientFunds));
        Assert.Equal(Abstractions.Ledger.LedgerErrors.InsufficientFundsCode, DeclineReasons.InsufficientFundsCode);
    }

    private CardAuthorization Approve()
    {
        return CardAuthorization.Approve(
            Guid.CreateVersion7(), _card, _merchantAccountId, Money.Create(30m, "BRL").Value, CreatedAt);
    }
}
