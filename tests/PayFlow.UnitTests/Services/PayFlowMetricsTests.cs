using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Domain.Entities;
using PayFlow.Infrastructure.Observability;
using PayFlow.Infrastructure.Persistence;

namespace PayFlow.UnitTests.Services;

public class PayFlowMetricsTests
{
    [Fact]
    public void RecordTransfer_RecordsMetricValuesWithoutThrowing()
    {
        // Arrange
        var metrics = new PayFlowMetrics();

        // Act & Assert
        var actCompleted = () => metrics.RecordTransfer("Completed", "EGP", 0.125);
        actCompleted.Should().NotThrow();

        var actFailed = () => metrics.RecordTransfer("Failed", "USD", 0.045);
        actFailed.Should().NotThrow();
    }

    [Fact]
    public void SetActiveWalletsCount_UpdatesGaugeCount()
    {
        // Arrange
        var metrics = new PayFlowMetrics();

        // Act
        metrics.SetActiveWalletsCount(42);

        // Assert
        metrics.GetActiveWalletsCount().Should().Be(42);

        // Act again
        metrics.SetActiveWalletsCount(100);
        metrics.GetActiveWalletsCount().Should().Be(100);
    }

    [Fact]
    public void SetActiveWalletsCount_WithNegativeValue_ClampsToZero()
    {
        // Arrange
        var metrics = new PayFlowMetrics();

        // Act
        metrics.SetActiveWalletsCount(-10);

        // Assert
        metrics.GetActiveWalletsCount().Should().Be(0);
    }

    [Fact]
    public void ActiveWalletsGauge_WhenMeterScraped_ReadsFromDbContext()
    {
        // Arrange
        var dbOptions = new DbContextOptionsBuilder<PayFlowDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var dbContext = new PayFlowDbContext(dbOptions);
        dbContext.Wallets.AddRange(
            new Wallet { Balance = 100m, Currency = "EGP", IsFrozen = false },
            new Wallet { Balance = 200m, Currency = "USD", IsFrozen = false },
            new Wallet { Balance = 50m, Currency = "EUR", IsFrozen = true } // frozen
        );
        dbContext.SaveChanges();

        var services = new ServiceCollection();
        services.AddScoped<IPayFlowDbContext>(_ => dbContext);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var metrics = new PayFlowMetrics(scopeFactory);

        // Act - meter creates observable gauge
        // Let's use a MeterListener to trigger the observable gauge measurement callback
        long measuredValue = -1;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == PayFlowMetrics.ActiveWalletsCountMetric)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            measuredValue = measurement;
        });
        listener.Start();
        listener.RecordObservableInstruments();

        // Assert - 2 active non-frozen wallets out of 3 total
        measuredValue.Should().Be(2);
    }
}
