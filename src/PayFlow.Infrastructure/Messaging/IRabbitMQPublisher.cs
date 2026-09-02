namespace PayFlow.Infrastructure.Messaging;

public interface IRabbitMQPublisher
{
    Task PublishEventAsync(string routingKey, string messagePayload, CancellationToken cancellationToken = default);
}
