using FluentAssertions;
using PayFlow.Domain.Entities;
using PayFlow.Domain.Exceptions;
using Xunit;

namespace PayFlow.UnitTests.Domain;

public class WalletTests
{
    [Fact]
    public void Deposit_PositiveAmount_IncreasesBalance()
    {
        // Arrange
        var wallet = new Wallet { Balance = 100.00m };

        // Act
        wallet.Deposit(50.00m);

        // Assert
        wallet.Balance.Should().Be(150.00m);
    }

    [Fact]
    public void Deposit_ZeroOrNegativeAmount_ThrowsInvalidTransactionException()
    {
        // Arrange
        var wallet = new Wallet { Balance = 100.00m };

        // Act
        var actZero = () => wallet.Deposit(0m);
        var actNegative = () => wallet.Deposit(-10m);

        // Assert
        actZero.Should().Throw<InvalidTransactionException>();
        actNegative.Should().Throw<InvalidTransactionException>();
    }

    [Fact]
    public void Withdraw_ValidAmount_DecreasesBalance()
    {
        // Arrange
        var wallet = new Wallet { Balance = 100.00m };

        // Act
        wallet.Withdraw(40.00m);

        // Assert
        wallet.Balance.Should().Be(60.00m);
    }

    [Fact]
    public void Withdraw_InsufficientBalance_ThrowsInsufficientBalanceException()
    {
        // Arrange
        var wallet = new Wallet { Balance = 100.00m };

        // Act
        var act = () => wallet.Withdraw(150.00m);

        // Assert
        act.Should().Throw<InsufficientBalanceException>()
            .WithMessage("*Insufficient wallet balance*");
    }

    [Fact]
    public void Withdraw_ZeroOrNegativeAmount_ThrowsInvalidTransactionException()
    {
        // Arrange
        var wallet = new Wallet { Balance = 100.00m };

        // Act
        var actZero = () => wallet.Withdraw(0m);
        var actNegative = () => wallet.Withdraw(-10m);

        // Assert
        actZero.Should().Throw<InvalidTransactionException>();
        actNegative.Should().Throw<InvalidTransactionException>();
    }
}
