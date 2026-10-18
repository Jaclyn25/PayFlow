namespace PayFlow.Application.Common.Interfaces;

public record ExchangeRateQuoteResponse(
    string SourceCurrency,
    string TargetCurrency,
    decimal ExchangeRate,
    decimal SourceAmount,
    decimal TargetAmount,
    DateTime TimestampUtc
);

public interface IExchangeRateService
{
    Task<decimal> GetExchangeRateAsync(
        string sourceCurrency,
        string targetCurrency,
        CancellationToken cancellationToken = default);

    Task<ExchangeRateQuoteResponse> GetQuoteAsync(
        string sourceCurrency,
        string targetCurrency,
        decimal sourceAmount,
        CancellationToken cancellationToken = default);

    IReadOnlyDictionary<string, decimal> GetSupportedRatesAgainstUsd();
}
