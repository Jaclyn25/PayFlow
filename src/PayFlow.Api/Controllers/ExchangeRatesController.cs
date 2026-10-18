using Microsoft.AspNetCore.Mvc;
using PayFlow.Application.Common.Interfaces;

namespace PayFlow.Api.Controllers;

[ApiController]
[Route("api/exchange-rates")]
public class ExchangeRatesController : ControllerBase
{
    private readonly IExchangeRateService _exchangeRateService;

    public ExchangeRatesController(IExchangeRateService exchangeRateService)
    {
        _exchangeRateService = exchangeRateService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyDictionary<string, decimal>), StatusCodes.Status200OK)]
    public IActionResult GetRates()
    {
        var rates = _exchangeRateService.GetSupportedRatesAgainstUsd();
        return Ok(rates);
    }

    [HttpGet("quote")]
    [ProducesResponseType(typeof(ExchangeRateQuoteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetQuote(
        [FromQuery] string sourceCurrency,
        [FromQuery] string targetCurrency,
        [FromQuery] decimal amount,
        CancellationToken cancellationToken)
    {
        if (amount <= 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid Amount",
                Detail = "Amount must be greater than zero.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var quote = await _exchangeRateService.GetQuoteAsync(sourceCurrency, targetCurrency, amount, cancellationToken);
        return Ok(quote);
    }
}
