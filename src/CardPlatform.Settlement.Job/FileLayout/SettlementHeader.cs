namespace CardPlatform.Settlement.Job.FileLayout;

/// <summary>
/// Header of a settlement file: the file covers one settlement date and one currency.
/// </summary>
/// <param name="SettlementDate">The settlement date the file covers.</param>
/// <param name="Currency">ISO 4217 code of the currency of every amount in the file.</param>
public sealed record SettlementHeader(DateOnly SettlementDate, string Currency);
