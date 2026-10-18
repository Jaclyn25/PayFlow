using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Domain.Constants;
using PayFlow.Domain.Exceptions;
using PayFlow.Infrastructure.Services;
using Xunit;

namespace PayFlow.UnitTests.Services;

public class ExchangeRateServiceTests
{
    private readonly Mock<ICacheService> _cacheServiceMock = new();
    private readonly Mock<ILogger<ExchangeRateService>> _loggerMock = new();
    private readonly ExchangeRateService _service;

    public ExchangeRateServiceTests()
    {
        _service = new ExchangeRateService(_cacheServiceMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task GetExchangeRateAsync_SameCurrency_ReturnsOne()
    {
        // Act
        var rate = await _service.GetExchangeRateAsync("USD", "USD");

        // Assert
        rate.Should().Be(1.0m);
    }

    [Fact]
    public async Task GetExchangeRateAsync_UsdToEgp_ReturnsDirectRate()
    {
        // Act
        var rate = await _service.GetExchangeRateAsync("USD", "EGP");

        // Assert
        rate.Should().Be(48.500000m);
    }

    [Fact]
    public async Task GetExchangeRateAsync_EurToGbp_CalculatesCrossRateAccurately()
    {
        // Arrange: USD->EUR is 0.92, USD->GBP is 0.79 => EUR->GBP is 0.79 / 0.92 = 0.858696
        // Act
        var rate = await _service.GetExchangeRateAsync("EUR", "GBP");

        // Assert
        rate.Should().Be(0.858696m);
    }

    [Fact]
    public async Task GetExchangeRateAsync_UnsupportedCurrency_ThrowsInvalidTransactionException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<InvalidTransactionException>(() =>
            _service.GetExchangeRateAsync("XYZ", "USD"));
    }

    [Fact]
    public async Task GetQuoteAsync_ValidCurrencies_ReturnsAccurateTargetAmount()
    {
        // Arrange: $100 USD to EGP at 48.50
        // Act
        var quote = await _service.GetQuoteAsync("USD", "EGP", 100.00m);

        // Assert
        quote.Should().NotBeNull();
        quote.SourceCurrency.Should().Be("USD");
        quote.TargetCurrency.Should().Be("EGP");
        quote.SourceAmount.Should().Be(100.00m);
        quote.ExchangeRate.Should().Be(48.500000m);
        quote.TargetAmount.Should().Be(4850.00m);
    }
}
