using CardPlatform.Ledger.Application.Common;
using CardPlatform.Ledger.Domain.Accounts;

namespace CardPlatform.Ledger.Application.Accounts;

public sealed record AccountDto(Guid Id, AccountType Type, string Currency, MoneyDto Balance);
