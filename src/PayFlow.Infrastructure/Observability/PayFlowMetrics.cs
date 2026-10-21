using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using PayFlow.Application.Common.Interfaces;

namespace PayFlow.Infrastructure.Observability;

public class PayFlowMetrics : IPayFlowMetrics
{
    public const string TransfersTotalMetric = "payflow_transfers_total";
    public const string TransferDurationMetric = "payflow_transfer_duration_seconds";
    public const string ActiveWalletsCountMetric = "payflow_active_wallets_count";

    private readonly Counter<long> _transfersTotal;
    private readonly Histogram<double> _transferDurationSeconds;
    private readonly IServiceScopeFactory? _scopeFactory;
    private long _activeWalletsCount;

    public PayFlowMetrics(IServiceScopeFactory? scopeFactory = null)
    {
        _scopeFactory = scopeFactory;
        var meter = new Meter(PayFlowDiagnostics.MeterName, "1.0.0");

        _transfersTotal = meter.CreateCounter<long>(
            TransfersTotalMetric,
            unit: "transfers",
            description: "Total number of transfers processed, tagged by status and currency.");

        _transferDurationSeconds = meter.CreateHistogram<double>(
            TransferDurationMetric,
            unit: "s",
            description: "Duration of transfer execution in seconds.");

        meter.CreateObservableGauge<long>(
            ActiveWalletsCountMetric,
            () =>
            {
                if (_scopeFactory != null)
                {
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var dbContext = scope.ServiceProvider.GetService<IPayFlowDbContext>();
                        if (dbContext != null)
                        {
                            var count = dbContext.Wallets.Count(w => !w.IsFrozen);
                            Interlocked.Exchange(ref _activeWalletsCount, count);
                            return count;
                        }
                    }
                    catch
                    {
                        // Fallback to in-memory cached value
                    }
                }
                return Interlocked.Read(ref _activeWalletsCount);
            },
            unit: "wallets",
            description: "Current number of active (non-frozen) wallets in the system.");
    }

    public void RecordTransfer(string status, string currency, double durationSeconds)
    {
        var tags = new TagList
        {
            { "status", status.ToLowerInvariant() },
            { "currency", currency.ToUpperInvariant() }
        };

        _transfersTotal.Add(1, tags);
        _transferDurationSeconds.Record(durationSeconds, tags);
    }

    public void SetActiveWalletsCount(long count)
    {
        Interlocked.Exchange(ref _activeWalletsCount, Math.Max(0, count));
    }

    public long GetActiveWalletsCount()
    {
        return Interlocked.Read(ref _activeWalletsCount);
    }
}
