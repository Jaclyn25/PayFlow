using System.Diagnostics;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PayFlow.Application.Common;
using PayFlow.Infrastructure.Messaging;
using PayFlow.Infrastructure.Observability;

namespace PayFlow.UnitTests.Services;

public class PayFlowTracingTests
{
    [Fact]
    public void PayFlowActivitySource_HasExpectedNameAndVersion()
    {
        PayFlowActivitySource.ActivitySourceName.Should().Be("PayFlow");
        PayFlowActivitySource.Instance.Name.Should().Be("PayFlow");
        PayFlowActivitySource.Instance.Version.Should().Be("1.0.0");
    }

    [Fact]
    public void ActivitySource_StartsChildActivitiesForEndToEndJourney()
    {
        // Arrange - enable ActivitySource listener
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "PayFlow",
            Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        // Act
        using var rootActivity = PayFlowActivitySource.Instance.StartActivity("TransferService.Transfer", ActivityKind.Internal);
        rootActivity.Should().NotBeNull();

        using var dbLockActivity = PayFlowActivitySource.Instance.StartActivity("DB Lock", ActivityKind.Internal);
        dbLockActivity.Should().NotBeNull();
        dbLockActivity!.ParentId.Should().Be(rootActivity!.Id);

        dbLockActivity.Dispose();

        using var outboxActivity = PayFlowActivitySource.Instance.StartActivity("Outbox Insert", ActivityKind.Internal);
        outboxActivity.Should().NotBeNull();
        outboxActivity!.ParentId.Should().Be(rootActivity!.Id);
    }

    [Fact]
    public void Traceparent_CanBeFormattedAndParsedCorrectly()
    {
        // Arrange
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "PayFlow",
            Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        using var producerActivity = PayFlowActivitySource.Instance.StartActivity("RabbitMQ.Publish", ActivityKind.Producer);
        producerActivity.Should().NotBeNull();
        var traceparentId = producerActivity!.Id!;

        // Act - consumer parses traceparent
        var parsed = ActivityContext.TryParse(traceparentId, null, out var parentContext);

        // Assert
        parsed.Should().BeTrue();
        parentContext.TraceId.Should().Be(producerActivity.TraceId);
        parentContext.SpanId.Should().Be(producerActivity.SpanId);

        using var consumerActivity = PayFlowActivitySource.Instance.StartActivity("RabbitMQ.Consume", ActivityKind.Consumer, parentContext);
        consumerActivity.Should().NotBeNull();
        consumerActivity!.ParentSpanId.Should().Be(producerActivity.SpanId);
    }
}
