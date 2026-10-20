using System.Text;
using FluentAssertions;
using PayFlow.Infrastructure.Messaging;
using RabbitMQ.Client;
using Xunit;

namespace PayFlow.UnitTests.Services;

public class DeadLetterQueueServiceTests
{
    [Fact]
    public void ParseXDeathHeader_NullHeaders_ReturnsDefaultFallback()
    {
        // Act
        var (reason, count, time) = DeadLetterQueueService.ParseXDeathHeader(null);

        // Assert
        reason.Should().Be("rejected");
        count.Should().Be(1);
        time.Should().BeNull();
    }

    [Fact]
    public void ParseXDeathHeader_ValidXDeathHeader_ExtractsMetadataCorrectly()
    {
        // Arrange
        var timestamp = new AmqpTimestamp(1729450000); // Unix timestamp
        var entry = new Dictionary<string, object?>
        {
            { "reason", Encoding.UTF8.GetBytes("rejected") },
            { "count", 3 },
            { "time", timestamp }
        };

        var headers = new Dictionary<string, object?>
        {
            { "x-death", new List<object> { entry } }
        };

        // Act
        var (reason, count, time) = DeadLetterQueueService.ParseXDeathHeader(headers);

        // Assert
        reason.Should().Be("rejected");
        count.Should().Be(3);
        time.Should().NotBeNull();
        time.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1729450000).UtcDateTime);
    }

    [Fact]
    public void ParseXDeathHeader_StringReason_ExtractsMetadataCorrectly()
    {
        // Arrange
        var entry = new Dictionary<string, object?>
        {
            { "reason", "expired" },
            { "count", 5 }
        };

        var headers = new Dictionary<string, object?>
        {
            { "x-death", new List<object> { entry } }
        };

        // Act
        var (reason, count, time) = DeadLetterQueueService.ParseXDeathHeader(headers);

        // Assert
        reason.Should().Be("expired");
        count.Should().Be(5);
        time.Should().BeNull();
    }
}
