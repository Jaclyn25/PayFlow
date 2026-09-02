using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Infrastructure.Messaging;

namespace PayFlow.Infrastructure.BackgroundJobs;

public class OutboxBackgroundWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxBackgroundWorker> _logger;
    private readonly TimeSpan _period = TimeSpan.FromSeconds(5);

    public OutboxBackgroundWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxBackgroundWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Outbox Background Worker started.");

        using var timer = new PeriodicTimer(_period);
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessOutboxMessagesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unhandled error occurred during Outbox processing.");
            }
        }
    }

    private async Task ProcessOutboxMessagesAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IPayFlowDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IRabbitMQPublisher>();

        var pendingMessages = await dbContext.OutboxMessages
            .Where(m => m.ProcessedAtUtc == null)
            .OrderBy(m => m.CreatedAtUtc)
            .Take(20)
            .ToListAsync(cancellationToken);

        if (pendingMessages.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Processing {Count} pending outbox message(s).", pendingMessages.Count);

        foreach (var message in pendingMessages)
        {
            try
            {
                var routingKey = message.Type switch
                {
                    "TransferCompleted" => "transfer.completed",
                    _ => "domain.event"
                };

                await publisher.PublishEventAsync(routingKey, message.Payload, cancellationToken);

                message.ProcessedAtUtc = DateTime.UtcNow;
                message.RetryCount++;
                message.Error = null;
            }
            catch (Exception ex)
            {
                message.RetryCount++;
                message.Error = ex.Message;
                _logger.LogWarning(ex, "Failed to publish outbox message ID '{Id}'. Attempt {RetryCount}.", message.Id, message.RetryCount);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
