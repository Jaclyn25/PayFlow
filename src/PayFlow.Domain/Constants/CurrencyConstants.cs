namespace PayFlow.Domain.Constants;

public static class CurrencyConstants
{
    public const string Usd = "USD";
    public const string Eur = "EUR";
    public const string Gbp = "GBP";
    public const string Egp = "EGP";

    public static readonly IReadOnlySet<string> SupportedCurrencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Usd, Eur, Gbp, Egp
    };

    public static bool IsSupported(string? currency)
    {
        return !string.IsNullOrWhiteSpace(currency) && SupportedCurrencies.Contains(currency);
    }
}
