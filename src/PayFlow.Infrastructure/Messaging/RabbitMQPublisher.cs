using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayFlow.Infrastructure.Observability;
using RabbitMQ.Client;

namespace PayFlow.Infrastructure.Messaging;

public class RabbitMQPublisher : IRabbitMQPublisher
{
    private readonly RabbitMQOptions _options;
    private readonly ILogger<RabbitMQPublisher> _logger;

    public RabbitMQPublisher(IOptions<RabbitMQOptions> options, ILogger<RabbitMQPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task PublishEventAsync(string routingKey, string messagePayload, CancellationToken cancellationToken = default)
    {
        using var activity = PayFlowDiagnostics.ActivitySource.StartActivity("RabbitMQ.Publish", ActivityKind.Producer);
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination", _options.ExchangeName);
        activity?.SetTag("messaging.routing_key", routingKey);

        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password
        };

        using var connection = await factory.CreateConnectionAsync(cancellationToken);
        using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(
            exchange: _options.ExchangeName,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);

        var body = Encoding.UTF8.GetBytes(messagePayload);

        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            Headers = new Dictionary<string, object?>()
        };

        if (activity != null && !string.IsNullOrEmpty(activity.Id))
        {
            properties.Headers["traceparent"] = Encoding.UTF8.GetBytes(activity.Id);
            if (!string.IsNullOrEmpty(activity.TraceStateString))
            {
                properties.Headers["tracestate"] = Encoding.UTF8.GetBytes(activity.TraceStateString);
            }
        }

        await channel.BasicPublishAsync(
            exchange: _options.ExchangeName,
            routingKey: routingKey,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);

        _logger.LogInformation("Published outbox message to RabbitMQ exchange '{Exchange}' with routing key '{RoutingKey}'.",
            _options.ExchangeName, routingKey);
    }
}
