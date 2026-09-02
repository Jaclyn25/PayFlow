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
}
