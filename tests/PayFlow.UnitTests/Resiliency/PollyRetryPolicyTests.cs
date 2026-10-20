using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PayFlow.Infrastructure.Messaging;
using Xunit;

namespace PayFlow.UnitTests.Resiliency;

public class PollyRetryPolicyTests
{
    private readonly Mock<ILogger> _loggerMock = new();

    [Fact]
    public async Task RetryPipeline_TransientFailure_RetriesAndEventuallySucceeds()
    {
        // Arrange
        var pipeline = ResiliencePolicies.CreateRabbitMqRetryPipeline(
            maxRetries: 3,
            initialDelaySeconds: 0.01, // Fast for tests
            logger: _loggerMock.Object);

        var attempts = 0;

        // Act
        await pipeline.ExecuteAsync(async _ =>
        {
            attempts++;
            if (attempts < 3)
            {
                throw new InvalidOperationException($"Transient network error attempt #{attempts}");
            }
            await Task.CompletedTask;
        });

        // Assert
        attempts.Should().Be(3);
    }

    [Fact]
    public async Task RetryPipeline_PermanentFailure_ExhaustsAllRetriesAndThrows()
    {
        // Arrange
        var pipeline = ResiliencePolicies.CreateRabbitMqRetryPipeline(
            maxRetries: 3,
            initialDelaySeconds: 0.01,
            logger: _loggerMock.Object);

        var attempts = 0;

        // Act
        var act = () => pipeline.ExecuteAsync(async _ =>
        {
            attempts++;
            await Task.Yield();
            throw new InvalidOperationException("Fatal downstream outage");
        }).AsTask();

        // Assert: 1 initial attempt + 3 retries = 4 attempts total
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Fatal downstream outage");

        attempts.Should().Be(4);
    }

    [Fact]
    public async Task RetryPipeline_SuccessOnFirstAttempt_DoesNotRetry()
    {
        // Arrange
        var pipeline = ResiliencePolicies.CreateRabbitMqRetryPipeline(
            maxRetries: 3,
            initialDelaySeconds: 0.01,
            logger: _loggerMock.Object);

        var attempts = 0;

        // Act
        await pipeline.ExecuteAsync(async _ =>
        {
            attempts++;
            await Task.CompletedTask;
        });

        // Assert
        attempts.Should().Be(1);
    }
}
