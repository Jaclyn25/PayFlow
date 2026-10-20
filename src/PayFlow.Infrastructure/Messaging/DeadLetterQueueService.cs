using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Application.DTOs;
using RabbitMQ.Client;

namespace PayFlow.Infrastructure.Messaging;

public class DeadLetterQueueService : IDeadLetterQueueService
{
    private readonly RabbitMQOptions _options;
    private readonly ILogger<DeadLetterQueueService> _logger;

    public DeadLetterQueueService(
        IOptions<RabbitMQOptions> options,
        ILogger<DeadLetterQueueService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DeadLetterMessageDto>> GetDeadLetterMessagesAsync(int count = 20, CancellationToken cancellationToken = default)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password
        };

        using var connection = await factory.CreateConnectionAsync(cancellationToken);
        using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        var messages = new List<DeadLetterMessageDto>();
        var deliveryTagsToRequeue = new List<ulong>();

        try
        {
            for (var i = 0; i < count; i++)
            {
                var result = await channel.BasicGetAsync(_options.DeadLetterQueueName, autoAck: false, cancellationToken);
                if (result == null)
                {
                    break;
                }

                deliveryTagsToRequeue.Add(result.DeliveryTag);

                var payload = Encoding.UTF8.GetString(result.Body.ToArray());
                var (deathReason, deathCount, deadLetteredAtUtc) = ParseXDeathHeader(result.BasicProperties?.Headers);

                var headersDict = new Dictionary<string, string>();
                if (result.BasicProperties?.Headers != null)
                {
                    foreach (var kvp in result.BasicProperties.Headers)
                    {
                        if (kvp.Value is byte[] byteVal)
                        {
                            headersDict[kvp.Key] = Encoding.UTF8.GetString(byteVal);
                        }
                        else
                        {
                            headersDict[kvp.Key] = kvp.Value?.ToString() ?? string.Empty;
                        }
                    }
                }

                var messageId = result.BasicProperties?.MessageId ?? Guid.NewGuid().ToString();
                messages.Add(new DeadLetterMessageDto(
                    messageId,
                    payload,
                    result.RoutingKey,
                    deathReason,
                    deathCount,
                    deadLetteredAtUtc,
                    headersDict
                ));
            }
        }
        finally
        {
            // Requeue inspected messages back to DLQ so inspection doesn't drop them
            foreach (var deliveryTag in deliveryTagsToRequeue)
            {
                try
                {
                    await channel.BasicNackAsync(deliveryTag, multiple: false, requeue: true, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to requeue DLQ message with delivery tag {DeliveryTag}.", deliveryTag);
                }
            }
        }

        return messages;
    }

    public async Task<DlqRedeliveryResult> RedeliverDeadLetterMessagesAsync(int count = 10, CancellationToken cancellationToken = default)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password
        };

        using var connection = await factory.CreateConnectionAsync(cancellationToken);
        using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        var redeliveredCount = 0;

        for (var i = 0; i < count; i++)
        {
            var result = await channel.BasicGetAsync(_options.DeadLetterQueueName, autoAck: false, cancellationToken);
            if (result == null)
            {
                break;
            }

            try
            {
                // Re-publish to primary exchange with original routing key
                var targetRoutingKey = !string.IsNullOrWhiteSpace(result.RoutingKey) && result.RoutingKey != _options.DeadLetterRoutingKey
                    ? result.RoutingKey
                    : _options.RoutingKey;

                var props = new BasicProperties
                {
                    Persistent = true,
                    ContentType = result.BasicProperties?.ContentType ?? "application/json",
                    MessageId = result.BasicProperties?.MessageId
                };

                await channel.BasicPublishAsync(
                    exchange: _options.ExchangeName,
                    routingKey: targetRoutingKey,
                    mandatory: false,
                    basicProperties: props,
                    body: result.Body,
                    cancellationToken: cancellationToken);

                // Acknowledge removal from DLQ
                await channel.BasicAckAsync(result.DeliveryTag, multiple: false, cancellationToken);
                redeliveredCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to redeliver message {DeliveryTag} from DLQ. Requeuing in DLQ.", result.DeliveryTag);
                await channel.BasicNackAsync(result.DeliveryTag, multiple: false, requeue: true, cancellationToken);
            }
        }

        return new DlqRedeliveryResult(
            redeliveredCount,
            $"Successfully redelivered {redeliveredCount} message(s) from DLQ to exchange '{_options.ExchangeName}'."
        );
    }

    public async Task<uint> PurgeDeadLetterQueueAsync(CancellationToken cancellationToken = default)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password
        };

        using var connection = await factory.CreateConnectionAsync(cancellationToken);
        using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        var count = await channel.QueuePurgeAsync(_options.DeadLetterQueueName, cancellationToken);
        _logger.LogInformation("Purged {Count} messages from Dead Letter Queue '{Queue}'.", count, _options.DeadLetterQueueName);
        return count;
    }

    public static (string? Reason, int Count, DateTime? DeadLetteredAtUtc) ParseXDeathHeader(IDictionary<string, object?>? headers)
    {
        if (headers == null || !headers.TryGetValue("x-death", out var xDeathObj) || xDeathObj == null)
        {
            return ("rejected", 1, null);
        }

        try
        {
            if (xDeathObj is IList<object> list && list.Count > 0 && list[0] is IDictionary<string, object?> firstEntry)
            {
                var reason = firstEntry.TryGetValue("reason", out var rVal)
                    ? (rVal is byte[] rBytes ? Encoding.UTF8.GetString(rBytes) : rVal?.ToString())
                    : "rejected";

                var count = firstEntry.TryGetValue("count", out var cVal) && cVal != null
                    ? Convert.ToInt32(cVal)
                    : 1;

                DateTime? time = null;
                if (firstEntry.TryGetValue("time", out var tVal) && tVal is AmqpTimestamp ts)
                {
                    time = DateTimeOffset.FromUnixTimeSeconds(ts.UnixTime).UtcDateTime;
                }

                return (reason, count, time);
            }
        }
        catch
        {
            // Fallback
        }

        return ("rejected", 1, null);
    }
}
