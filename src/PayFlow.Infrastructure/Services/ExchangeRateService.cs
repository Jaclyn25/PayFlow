using Microsoft.Extensions.Logging;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Domain.Constants;
using PayFlow.Domain.Exceptions;

namespace PayFlow.Infrastructure.Services;

public class ExchangeRateService : IExchangeRateService
{
    private readonly ICacheService _cacheService;
    private readonly ILogger<ExchangeRateService> _logger;

    // Standard benchmark exchange rates anchored to 1 USD
    private static readonly Dictionary<string, decimal> RatesAgainstUsd = new(StringComparer.OrdinalIgnoreCase)
    {
        [CurrencyConstants.Usd] = 1.000000m,
        [CurrencyConstants.Eur] = 0.920000m,
        [CurrencyConstants.Gbp] = 0.790000m,
        [CurrencyConstants.Egp] = 48.500000m
    };

    public ExchangeRateService(ICacheService cacheService, ILogger<ExchangeRateService> logger)
    {
        _cacheService = cacheService;
        _logger = logger;
    }

    public async Task<decimal> GetExchangeRateAsync(
        string sourceCurrency,
        string targetCurrency,
        CancellationToken cancellationToken = default)
    {
        var src = sourceCurrency.Trim().ToUpperInvariant();
        var tgt = targetCurrency.Trim().ToUpperInvariant();

        if (src.Equals(tgt, StringComparison.OrdinalIgnoreCase))
        {
            return 1.0m;
        }

        if (!CurrencyConstants.IsSupported(src))
        {
            throw new InvalidTransactionException($"Source currency '{sourceCurrency}' is not supported.");
        }

        if (!CurrencyConstants.IsSupported(tgt))
        {
            throw new InvalidTransactionException($"Target currency '{targetCurrency}' is not supported.");
        }

        var cacheKey = $"fx:rate:{src}:{tgt}";
        var cachedRate = await _cacheService.GetAsync<decimal?>(cacheKey, cancellationToken);
        if (cachedRate.HasValue && cachedRate.Value > 0)
        {
            return cachedRate.Value;
        }

        var usdToSource = RatesAgainstUsd[src];
        var usdToTarget = RatesAgainstUsd[tgt];

        var rate = Math.Round(usdToTarget / usdToSource, 6);

        await _cacheService.SetAsync(cacheKey, rate, TimeSpan.FromHours(1), cancellationToken);

        _logger.LogInformation("FX Rate computed & cached: {Source}/{Target} = {Rate}", src, tgt, rate);

        return rate;
    }

    public async Task<ExchangeRateQuoteResponse> GetQuoteAsync(
        string sourceCurrency,
        string targetCurrency,
        decimal sourceAmount,
        CancellationToken cancellationToken = default)
    {
        if (sourceAmount <= 0)
        {
            throw new InvalidTransactionException("Source amount must be greater than zero for currency quote.");
        }

        var src = sourceCurrency.Trim().ToUpperInvariant();
        var tgt = targetCurrency.Trim().ToUpperInvariant();
        var rate = await GetExchangeRateAsync(src, tgt, cancellationToken);
        var targetAmount = Math.Round(sourceAmount * rate, 2);

        return new ExchangeRateQuoteResponse(
            src,
            tgt,
            rate,
            sourceAmount,
            targetAmount,
            DateTime.UtcNow
        );
    }

    public IReadOnlyDictionary<string, decimal> GetSupportedRatesAgainstUsd()
    {
        return RatesAgainstUsd;
    }
}
