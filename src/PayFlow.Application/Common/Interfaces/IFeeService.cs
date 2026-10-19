using PayFlow.Application.DTOs;

namespace PayFlow.Application.Common.Interfaces;

public interface IFeeService
{
    decimal CalculateFee(decimal amount, string currency = "EGP");
    FeeQuoteResponse GetFeeQuote(decimal amount, string currency = "EGP");
}
