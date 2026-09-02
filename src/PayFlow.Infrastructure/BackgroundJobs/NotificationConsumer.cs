using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Domain.Entities;
using PayFlow.Infrastructure.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace PayFlow.Infrastructure.BackgroundJobs;

public class NotificationConsumer : BackgroundService
{
    private readonly RabbitMQOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NotificationConsumer> _logger;

    public NotificationConsumer(
        IOptions<RabbitMQOptions> options,
        IServiceScopeFactory scopeFactory,
        ILogger<NotificationConsumer> logger)
    {
        _options = options.Value;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Notification Consumer background worker starting...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var factory = new ConnectionFactory
                {
                    HostName = _options.HostName,
                    Port = _options.Port,
                    UserName = _options.UserName,
                    Password = _options.Password
                };

                using var connection = await factory.CreateConnectionAsync(stoppingToken);
                using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

                await channel.ExchangeDeclareAsync(
                    exchange: _options.ExchangeName,
                    type: ExchangeType.Direct,
                    durable: true,
                    autoDelete: false,
                    cancellationToken: stoppingToken);

                await channel.QueueDeclareAsync(
                    queue: _options.QueueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    cancellationToken: stoppingToken);

                await channel.QueueBindAsync(
                    queue: _options.QueueName,
                    exchange: _options.ExchangeName,
                    routingKey: _options.RoutingKey,
                    cancellationToken: stoppingToken);

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += async (model, ea) =>
                {
                    var body = ea.Body.ToArray();
                    var message = Encoding.UTF8.GetString(body);

                    try
                    {
                        using var doc = JsonDocument.Parse(message);
                        var root = doc.RootElement;

                        var transactionId = root.TryGetProperty("TransactionId", out var idProp) ? idProp.GetString() : "UNKNOWN";
                        var reference = root.TryGetProperty("Reference", out var refProp) ? refProp.GetString() : "N/A";
                        var amount = root.TryGetProperty("Amount", out var amtProp) ? amtProp.GetDecimal() : 0m;
                        var currency = root.TryGetProperty("Currency", out var curProp) ? curProp.GetString() : "EGP";

                        using var scope = _scopeFactory.CreateScope();
                        var dbContext = scope.ServiceProvider.GetRequiredService<IPayFlowDbContext>();

                        // Durable Inbox Pattern Idempotency Check
                        var alreadyProcessed = await dbContext.InboxMessages
                            .AnyAsync(i => i.MessageId == transactionId, stoppingToken);

                        if (alreadyProcessed)
                        {
                            _logger.LogInformation("[NOTIFICATION CONSUMER] Duplicate event skipped for Transaction '{TransactionId}'.", transactionId);
                            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
                            return;
                        }

                        var inboxRecord = new InboxMessage
                        {
                            MessageId = transactionId!,
                            EventType = "TransferCompleted",
                            ProcessedAtUtc = DateTime.UtcNow
                        };

                        dbContext.InboxMessages.Add(inboxRecord);
                        await dbContext.SaveChangesAsync(stoppingToken);

                        _logger.LogInformation("[NOTIFICATION CONSUMER] Transfer notification sent for transaction '{Reference}' (ID: {TransactionId}). Amount: {Amount:F2} {Currency}.",
                            reference, transactionId, amount, currency);

                        await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
                    }
                    catch (DbUpdateException)
                    {
                        // Concurrent delivery race condition handled by unique index constraint
                        _logger.LogInformation("[NOTIFICATION CONSUMER] Race condition caught: event already processed for message.");
                        await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[NOTIFICATION CONSUMER] Error processing received notification message.");
                        await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
                    }
                };

                await channel.BasicConsumeAsync(
                    queue: _options.QueueName,
                    autoAck: false,
                    consumer: consumer,
                    cancellationToken: stoppingToken);

                _logger.LogInformation("Notification Consumer listening on queue '{Queue}'.", _options.QueueName);

                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Notification Consumer unable to connect to RabbitMQ ({Message}). Retrying in 10 seconds...", ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }
}
