using FluentAssertions;
using Microsoft.Extensions.Options;
using PayFlow.Application.Common.Options;
using PayFlow.Application.Services;
using Xunit;

namespace PayFlow.UnitTests.Services;

public class FeeServiceTests
{
    [Fact]
    public void CalculateFee_DefaultConfig_ReturnsFixedPlusPercentageFee()
    {
        // Arrange: 1% + 0.30
        var options = Options.Create(new FeeOptions
        {
            Enabled = true,
            Percentage = 0.01m,
            FixedFee = 0.30m,
            CapAmount = 50.00m,
            MinFee = 0.00m
        });

        var feeService = new FeeService(options);

        // Act: 100 * 0.01 + 0.30 = 1.30
        var fee = feeService.CalculateFee(100m, "EGP");

        // Assert
        fee.Should().Be(1.30m);
    }

    [Fact]
    public void CalculateFee_ExceedingCap_CapsAtMaximumAmount()
    {
        // Arrange
        var options = Options.Create(new FeeOptions
        {
            Enabled = true,
            Percentage = 0.01m,
            FixedFee = 0.30m,
            CapAmount = 50.00m,
            MinFee = 0.00m
        });

        var feeService = new FeeService(options);

        // Act: 10000 * 0.01 + 0.30 = 100.30 -> capped at 50.00
        var fee = feeService.CalculateFee(10000m, "USD");

        // Assert
        fee.Should().Be(50.00m);
    }

    [Fact]
    public void CalculateFee_WhenDisabled_ReturnsZero()
    {
        // Arrange
        var options = Options.Create(new FeeOptions
        {
            Enabled = false,
            Percentage = 0.02m,
            FixedFee = 1.00m
        });

        var feeService = new FeeService(options);

        // Act
        var fee = feeService.CalculateFee(500m, "EGP");

        // Assert
        fee.Should().Be(0.00m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-50)]
    public void CalculateFee_ZeroOrNegativeAmount_ReturnsZero(decimal amount)
    {
        var options = Options.Create(new FeeOptions { Enabled = true });
        var feeService = new FeeService(options);

        var fee = feeService.CalculateFee(amount, "EGP");

        fee.Should().Be(0.00m);
    }

    [Fact]
    public void GetFeeQuote_ReturnsAccurateBreakdown()
    {
        // Arrange
        var options = Options.Create(new FeeOptions
        {
            Enabled = true,
            Percentage = 0.015m, // 1.5%
            FixedFee = 0.50m,
            CapAmount = 100.00m
        });

        var feeService = new FeeService(options);

        // Act: 200 * 0.015 + 0.50 = 3.50
        var quote = feeService.GetFeeQuote(200m, "USD");

        // Assert
        quote.Amount.Should().Be(200m);
        quote.FeeAmount.Should().Be(3.50m);
        quote.GrossAmount.Should().Be(203.50m);
        quote.NetAmount.Should().Be(200m);
        quote.Currency.Should().Be("USD");
        quote.Percentage.Should().Be(0.015m);
        quote.FixedFee.Should().Be(0.50m);
        quote.CapAmount.Should().Be(100.00m);
    }
}
