namespace PayFlow.Infrastructure.Messaging;

public class RabbitMQOptions
{
    public const string SectionName = "RabbitMQ";

    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string ExchangeName { get; set; } = "payflow.events";
    public string QueueName { get; set; } = "payflow.notifications";
    public string RoutingKey { get; set; } = "transfer.completed";

    // Dead Letter Exchange & Queue Configuration
    public string DeadLetterExchangeName { get; set; } = "payflow.dlx";
    public string DeadLetterQueueName { get; set; } = "payflow.notifications.dlq";
    public string DeadLetterRoutingKey { get; set; } = "payflow.notifications.dlq";
    public int MaxRetryAttempts { get; set; } = 3;
    public double InitialRetryDelaySeconds { get; set; } = 2.0;
}
