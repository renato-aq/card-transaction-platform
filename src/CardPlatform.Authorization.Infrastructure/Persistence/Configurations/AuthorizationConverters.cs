using CardPlatform.Authorization.Domain.Monetary;

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CardPlatform.Authorization.Infrastructure.Persistence.Configurations;

/// <summary>
/// Value converters and column types shared by the entity configurations.
/// </summary>
internal static class AuthorizationConverters
{
    /// <summary>Column type for every amount: two decimal places, as enforced by <see cref="Money"/>.</summary>
    public const string AmountColumnType = "numeric(18,2)";

    /// <summary>Column type for ISO 4217 currency codes.</summary>
    public const string CurrencyColumnType = "char(3)";

    /// <summary>Maximum length of enum values stored as strings.</summary>
    public const int EnumMaxLength = 20;

    /// <summary>
    /// Stores a <see cref="Currency"/> as its code. Values read from the database were validated when written,
    /// so an invalid code is a corrupted row and throws.
    /// </summary>
    public static readonly ValueConverter<Currency, string> Currency = new(
        currency => currency.Code,
        code => Domain.Monetary.Currency.Create(code).Value);

    /// <summary>
    /// PostgreSQL <c>timestamptz</c> only accepts UTC offsets in Npgsql, so instants are stored in UTC
    /// (the instant is preserved; the original offset is not).
    /// </summary>
    public static readonly ValueConverter<DateTimeOffset, DateTimeOffset> Utc = new(
        value => value.ToUniversalTime(),
        value => value);
}
