using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PayFlow.Infrastructure.Messaging;
using PayFlow.IntegrationTests.Fixtures;
using RabbitMQ.Client;
using Xunit;

namespace PayFlow.IntegrationTests;

public class RabbitMqDlqIntegrationTests : IClassFixture<RabbitMqTestContainerFixture>
{
    private readonly RabbitMqTestContainerFixture _fixture;

    public RabbitMqDlqIntegrationTests(RabbitMqTestContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RealRabbitMQ_RejectedMessageWithoutRequeue_ArrivesInDeadLetterQueue()
    {
        var options = new RabbitMQOptions
        {
            HostName = _fixture.HostName,
            Port = _fixture.Port,
            UserName = _fixture.UserName,
            Password = _fixture.Password,
            ExchangeName = "test.events",
            QueueName = "test.notifications",
            RoutingKey = "transfer.completed",
            DeadLetterExchangeName = "test.dlx",
            DeadLetterQueueName = "test.notifications.dlq",
            DeadLetterRoutingKey = "test.notifications.dlq",
            MaxRetryAttempts = 3,
            InitialRetryDelaySeconds = 0.05
        };

        var factory = new ConnectionFactory
        {
            HostName = options.HostName,
            Port = options.Port,
            UserName = options.UserName,
            Password = options.Password
        };

        using var connection = await factory.CreateConnectionAsync();
        using var channel = await connection.CreateChannelAsync();

        // 1. Setup DLX and DLQ
        await channel.ExchangeDeclareAsync(options.DeadLetterExchangeName, ExchangeType.Direct, durable: true);
        await channel.QueueDeclareAsync(options.DeadLetterQueueName, durable: true, exclusive: false, autoDelete: false);
        await channel.QueueBindAsync(options.DeadLetterQueueName, options.DeadLetterExchangeName, options.DeadLetterRoutingKey);

        // 2. Setup Primary Queue with x-dead-letter-exchange
        var queueArgs = new Dictionary<string, object?>
        {
            { "x-dead-letter-exchange", options.DeadLetterExchangeName },
            { "x-dead-letter-routing-key", options.DeadLetterRoutingKey }
        };

        await channel.ExchangeDeclareAsync(options.ExchangeName, ExchangeType.Direct, durable: true);
        await channel.QueueDeclareAsync(options.QueueName, durable: true, exclusive: false, autoDelete: false, arguments: queueArgs);
        await channel.QueueBindAsync(options.QueueName, options.ExchangeName, options.RoutingKey);

        // 3. Publish a test poison message
        var payload = "{\"TransactionId\":\"POISON-101\",\"Amount\":500.00}";
        var body = Encoding.UTF8.GetBytes(payload);
        var props = new BasicProperties { Persistent = true, ContentType = "application/json" };
        await channel.BasicPublishAsync(options.ExchangeName, options.RoutingKey, mandatory: false, basicProperties: props, body: body);

        // 4. Consume from primary queue and reject without requeue (simulating exhausted retries)
        var msgResult = await channel.BasicGetAsync(options.QueueName, autoAck: false);
        msgResult.Should().NotBeNull();
        await channel.BasicNackAsync(msgResult!.DeliveryTag, multiple: false, requeue: false);

        // 5. Verify message arrived intact in Dead Letter Queue
        await Task.Delay(200); // small delay for routing
        var dlqResult = await channel.BasicGetAsync(options.DeadLetterQueueName, autoAck: false);
        dlqResult.Should().NotBeNull("Rejected message must be forwarded to the Dead Letter Queue.");
        var dlqPayload = Encoding.UTF8.GetString(dlqResult!.Body.ToArray());
        dlqPayload.Should().Be(payload);

        // 6. Test DeadLetterQueueService inspection and redelivery
        var dlqService = new DeadLetterQueueService(Options.Create(options), NullLogger<DeadLetterQueueService>.Instance);
        
        // Requeue for service to read
        await channel.BasicNackAsync(dlqResult.DeliveryTag, multiple: false, requeue: true);

        var inspected = await dlqService.GetDeadLetterMessagesAsync(5);
        inspected.Should().NotBeEmpty();
        inspected[0].Payload.Should().Be(payload);

        // Redeliver
        var redelivery = await dlqService.RedeliverDeadLetterMessagesAsync(1);
        redelivery.RedeliveredCount.Should().Be(1);

        // Verify message is back in primary queue
        var returnedMsg = await channel.BasicGetAsync(options.QueueName, autoAck: true);
        returnedMsg.Should().NotBeNull();
        Encoding.UTF8.GetString(returnedMsg!.Body.ToArray()).Should().Be(payload);
    }
}
