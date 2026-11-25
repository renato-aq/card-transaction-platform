using CardPlatform.Ledger.Application.Common;
using CardPlatform.Ledger.Domain.Accounts;

namespace CardPlatform.Ledger.Api.Endpoints;

/// <summary>Body of <c>POST /accounts</c>. <see cref="Type"/> is <c>card</c>, <c>merchant</c> or <c>settlement</c>.</summary>
public sealed record OpenAccountRequest(string? Type, string? Currency);

public sealed record MoneyResponse(decimal Amount, string Currency)
{
    internal static MoneyResponse From(MoneyDto money)
    {
        return new MoneyResponse(money.Amount, money.Currency);
    }
}

/// <summary>An account; <see cref="Type"/> is serialized in camelCase (<c>card</c>, <c>merchant</c>, <c>settlement</c>).</summary>
public sealed record AccountResponse(Guid Id, AccountType Type, string Currency, MoneyResponse Balance);

public sealed record AccountBalanceResponse(Guid AccountId, MoneyResponse Balance);
