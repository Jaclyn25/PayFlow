using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PayFlow.Application.Common.Interfaces;

namespace PayFlow.Infrastructure.BackgroundJobs;

public class WebhookDispatcherWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WebhookDispatcherWorker> _logger;
    private readonly TimeSpan _period = TimeSpan.FromSeconds(5);

    public WebhookDispatcherWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<WebhookDispatcherWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Webhook Dispatcher Background Worker started.");

        using var timer = new PeriodicTimer(_period);
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<IWebhookDispatcherService>();
                await dispatcher.ProcessPendingRetriesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unhandled error occurred during Webhook retry processing.");
            }
        }

        _logger.LogInformation("Webhook Dispatcher Background Worker stopping.");
    }
}
