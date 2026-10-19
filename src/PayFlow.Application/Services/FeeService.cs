using Microsoft.Extensions.Options;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Application.Common.Options;
using PayFlow.Application.DTOs;

namespace PayFlow.Application.Services;

public class FeeService : IFeeService
{
    private readonly FeeOptions _options;

    public FeeService(IOptions<FeeOptions> options)
    {
        _options = options.Value;
    }

    public decimal CalculateFee(decimal amount, string currency = "EGP")
    {
        if (!_options.Enabled || amount <= 0)
        {
            return 0.00m;
        }

        var calculatedFee = Math.Round((amount * _options.Percentage) + _options.FixedFee, 2);

        if (_options.CapAmount.HasValue && calculatedFee > _options.CapAmount.Value)
        {
            calculatedFee = _options.CapAmount.Value;
        }

        if (calculatedFee < _options.MinFee)
        {
            calculatedFee = _options.MinFee;
        }

        return calculatedFee;
    }

    public FeeQuoteResponse GetFeeQuote(decimal amount, string currency = "EGP")
    {
        var fee = CalculateFee(amount, currency);
        var gross = amount + fee;
        var net = amount;

        return new FeeQuoteResponse(
            Amount: amount,
            FeeAmount: fee,
            GrossAmount: gross,
            NetAmount: net,
            Currency: currency,
            Percentage: _options.Percentage,
            FixedFee: _options.FixedFee,
            CapAmount: _options.CapAmount
        );
    }
}
