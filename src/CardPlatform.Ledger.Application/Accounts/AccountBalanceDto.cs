using CardPlatform.Ledger.Application.Common;

namespace CardPlatform.Ledger.Application.Accounts;

public sealed record AccountBalanceDto(Guid AccountId, MoneyDto Balance);
