namespace CardPlatform.Settlement.Job.FileLayout;

/// <summary>
/// Number of minor-unit digits (the ISO 4217 exponent) of a currency: amounts are written to the settlement file in
/// minor units, e.g. cents for BRL. Most currencies use two digits; only the exceptions are listed here.
/// </summary>
public static class CurrencyMinorUnits
{
    public const int DefaultExponent = 2;

    private static readonly Dictionary<string, int> Exceptions = new(StringComparer.Ordinal)
    {
        ["BIF"] = 0, ["CLP"] = 0, ["DJF"] = 0, ["GNF"] = 0, ["ISK"] = 0, ["JPY"] = 0, ["KMF"] = 0, ["KRW"] = 0,
        ["PYG"] = 0, ["RWF"] = 0, ["UGX"] = 0, ["UYI"] = 0, ["VND"] = 0, ["VUV"] = 0, ["XAF"] = 0, ["XOF"] = 0,
        ["XPF"] = 0,
        ["BHD"] = 3, ["IQD"] = 3, ["JOD"] = 3, ["KWD"] = 3, ["LYD"] = 3, ["OMR"] = 3, ["TND"] = 3,
        ["CLF"] = 4, ["UYW"] = 4,
    };

    /// <summary>Returns how many minor-unit digits <paramref name="currency"/> has.</summary>
    public static int GetExponent(string currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        return Exceptions.TryGetValue(currency, out int exponent) ? exponent : DefaultExponent;
    }
}
