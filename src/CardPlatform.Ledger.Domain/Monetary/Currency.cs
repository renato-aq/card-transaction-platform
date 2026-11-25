using CardPlatform.Ledger.Domain.Common;

namespace CardPlatform.Ledger.Domain.Monetary;

/// <summary>
/// ISO 4217 currency code: exactly three uppercase ASCII letters (e.g. <c>BRL</c>, <c>USD</c>).
/// Only the format is validated; the code is not checked against the ISO 4217 list.
/// </summary>
public sealed record Currency
{
    public const string InvalidCurrencyCode = "invalid_currency";

    private const int CodeLength = 3;

    private Currency(string code)
    {
        Code = code;
    }

    public string Code { get; }

    public static Result<Currency> Create(string? code)
    {
        if (!IsValidCode(code))
        {
            return Result.Failure<Currency>(new Error(
                InvalidCurrencyCode,
                $"Currency '{code}' is not a valid ISO 4217 code (three uppercase letters)."));
        }

        return Result.Success(new Currency(code!));
    }

    public override string ToString()
    {
        return Code;
    }

    private static bool IsValidCode(string? code)
    {
        return code is { Length: CodeLength } && code.All(char.IsAsciiLetterUpper);
    }
}
