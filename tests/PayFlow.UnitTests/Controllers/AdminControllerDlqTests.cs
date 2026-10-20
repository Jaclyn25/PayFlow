using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PayFlow.Api.Controllers;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Application.DTOs;
using Xunit;

namespace PayFlow.UnitTests.Controllers;

public class AdminControllerDlqTests
{
    private readonly Mock<IAdminService> _adminServiceMock = new();
    private readonly Mock<IDeadLetterQueueService> _dlqServiceMock = new();
    private readonly AdminController _controller;

    public AdminControllerDlqTests()
    {
        _controller = new AdminController(_adminServiceMock.Object);
    }

    [Fact]
    public async Task GetDeadLetterMessages_ReturnsOkWithMessages()
    {
        // Arrange
        var mockMessages = new List<DeadLetterMessageDto>
        {
            new("MSG-1", "{\"Amount\":100}", "transfer.completed", "rejected", 3, DateTime.UtcNow, null)
        };

        _dlqServiceMock.Setup(d => d.GetDeadLetterMessagesAsync(20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockMessages);

        // Act
        var result = await _controller.GetDeadLetterMessages(20, _dlqServiceMock.Object, CancellationToken.None);

        // Assert
        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();
        var returnedMessages = okResult!.Value as IReadOnlyList<DeadLetterMessageDto>;
        returnedMessages.Should().HaveCount(1);
        returnedMessages![0].MessageId.Should().Be("MSG-1");
    }

    [Fact]
    public async Task RedeliverDeadLetterMessages_ReturnsOkWithResult()
    {
        // Arrange
        var mockResult = new DlqRedeliveryResult(5, "Redelivered 5 messages.");
        _dlqServiceMock.Setup(d => d.RedeliverDeadLetterMessagesAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResult);

        // Act
        var result = await _controller.RedeliverDeadLetterMessages(10, _dlqServiceMock.Object, CancellationToken.None);

        // Assert
        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();
        var returned = okResult!.Value as DlqRedeliveryResult;
        returned.Should().NotBeNull();
        returned!.RedeliveredCount.Should().Be(5);
    }

    [Fact]
    public async Task PurgeDeadLetterQueue_ReturnsOkWithPurgedCount()
    {
        // Arrange
        _dlqServiceMock.Setup(d => d.PurgeDeadLetterQueueAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(12u);

        // Act
        var result = await _controller.PurgeDeadLetterQueue(_dlqServiceMock.Object, CancellationToken.None);

        // Assert
        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();
        var returned = okResult!.Value as DlqPurgeResult;
        returned.Should().NotBeNull();
        returned!.PurgedCount.Should().Be(12u);
    }
}
