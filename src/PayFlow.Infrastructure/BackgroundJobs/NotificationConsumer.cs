using System.Diagnostics;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Infrastructure.Messaging;
using PayFlow.Infrastructure.Observability;
using Polly;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace PayFlow.Infrastructure.BackgroundJobs;

public class NotificationConsumer : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RabbitMQOptions _options;
    private readonly ResiliencePipeline _retryPipeline;
    private readonly ILogger<NotificationConsumer> _logger;

    public NotificationConsumer(
        IServiceScopeFactory scopeFactory,
        IOptions<RabbitMQOptions> options,
        ILogger<NotificationConsumer> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;

        // Build Polly resilience pipeline: Exponential backoff + randomized jitter retries
        _retryPipeline = ResiliencePolicies.CreateRabbitMqRetryPipeline(
            maxRetries: _options.MaxRetryAttempts,
            initialDelaySeconds: _options.InitialRetryDelaySeconds,
            logger: _logger);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Notification Consumer background service starting...");

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

                // 1. Declare Dead Letter Exchange (DLX) & Queue (DLQ)
                await channel.ExchangeDeclareAsync(
                    exchange: _options.DeadLetterExchangeName,
                    type: ExchangeType.Direct,
                    durable: true,
                    autoDelete: false,
                    cancellationToken: stoppingToken);

                await channel.QueueDeclareAsync(
                    queue: _options.DeadLetterQueueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    cancellationToken: stoppingToken);

                await channel.QueueBindAsync(
                    queue: _options.DeadLetterQueueName,
                    exchange: _options.DeadLetterExchangeName,
                    routingKey: _options.DeadLetterRoutingKey,
                    cancellationToken: stoppingToken);

                await channel.QueueBindAsync(
                    queue: _options.DeadLetterQueueName,
                    exchange: _options.DeadLetterExchangeName,
                    routingKey: _options.RoutingKey,
                    cancellationToken: stoppingToken);

                // 2. Declare Primary Exchange
                await channel.ExchangeDeclareAsync(
                    exchange: _options.ExchangeName,
                    type: ExchangeType.Direct,
                    durable: true,
                    autoDelete: false,
                    cancellationToken: stoppingToken);

                // 3. Declare Primary Queue with DLX configuration
                var queueArguments = new Dictionary<string, object?>
                {
                    { "x-dead-letter-exchange", _options.DeadLetterExchangeName },
                    { "x-dead-letter-routing-key", _options.DeadLetterRoutingKey }
                };

                await channel.QueueDeclareAsync(
                    queue: _options.QueueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: queueArguments,
                    cancellationToken: stoppingToken);

                await channel.QueueBindAsync(
                    queue: _options.QueueName,
                    exchange: _options.ExchangeName,
                    routingKey: _options.RoutingKey,
                    cancellationToken: stoppingToken);

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += async (model, ea) =>
                {
                    ActivityContext parentContext = default;
                    if (ea.BasicProperties?.Headers != null && ea.BasicProperties.Headers.TryGetValue("traceparent", out var tpObj))
                    {
                        var tpStr = tpObj switch
                        {
                            byte[] b => Encoding.UTF8.GetString(b),
                            string s => s,
                            _ => null
                        };

                        if (!string.IsNullOrEmpty(tpStr))
                        {
                            ActivityContext.TryParse(tpStr, null, out parentContext);
                        }
                    }

                    using var consumeActivity = PayFlowDiagnostics.ActivitySource.StartActivity(
                        "RabbitMQ.Consume",
                        ActivityKind.Consumer,
                        parentContext);

                    consumeActivity?.SetTag("messaging.system", "rabbitmq");
                    consumeActivity?.SetTag("messaging.destination", _options.QueueName);

                    var body = ea.Body.ToArray();
                    var message = Encoding.UTF8.GetString(body);

                    try
                    {
                        // Execute message processing under Polly exponential backoff + jitter retry policy
                        await _retryPipeline.ExecuteAsync(async ct =>
                        {
                            using var scope = _scopeFactory.CreateScope();
                            var processor = scope.ServiceProvider.GetRequiredService<INotificationMessageProcessor>();
                            await processor.ProcessAsync(message, ct);
                        }, stoppingToken);

                        await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
                    }
                    catch (DbUpdateException)
                    {
                        // Concurrent duplicate delivery race condition handled by unique index constraint on Inbox
                        _logger.LogInformation("[NOTIFICATION CONSUMER] Race condition caught: event already processed for message.");
                        await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "[NOTIFICATION CONSUMER] Exhausted all {MaxRetries} retries for message. Rejecting without requeue to DLX '{Dlx}'.",
                            _options.MaxRetryAttempts, _options.DeadLetterExchangeName);

                        // basic.nack with requeue: false causes RabbitMQ to route the message to x-dead-letter-exchange
                        await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                    }
                };

                await channel.BasicConsumeAsync(
                    queue: _options.QueueName,
                    autoAck: false,
                    consumer: consumer,
                    cancellationToken: stoppingToken);

                _logger.LogInformation("Notification Consumer listening on queue '{Queue}' with DLQ '{Dlq}'.",
                    _options.QueueName, _options.DeadLetterQueueName);

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
