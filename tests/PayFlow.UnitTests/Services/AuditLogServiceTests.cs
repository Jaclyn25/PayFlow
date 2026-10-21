using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Infrastructure.Persistence;
using PayFlow.Infrastructure.Services;
using Xunit;

namespace PayFlow.UnitTests.Services;

public class AuditLogServiceTests
{
    private readonly PayFlowDbContext _dbContext;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();
    private readonly Mock<ILogger<AuditLogService>> _loggerMock = new();
    private readonly AuditLogService _service;

    public AuditLogServiceTests()
    {
        var options = new DbContextOptionsBuilder<PayFlowDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new PayFlowDbContext(options);

        _currentUserServiceMock.Setup(c => c.IpAddress).Returns("192.168.1.100");
        _currentUserServiceMock.Setup(c => c.UserAgent).Returns("Mozilla/5.0 TestBrowser");

        _service = new AuditLogService(_dbContext, _currentUserServiceMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task LogAsync_ValidEvent_PersistsAuditLogRecord()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act
        await _service.LogAsync(userId, "UserLogin", userId.ToString(), true);

        // Assert
        var savedLog = await _dbContext.AuditLogs.FirstOrDefaultAsync(a => a.UserId == userId);
        savedLog.Should().NotBeNull();
        savedLog!.Action.Should().Be("UserLogin");
        savedLog.ResourceId.Should().Be(userId.ToString());
        savedLog.IsSuccess.Should().BeTrue();
        savedLog.IpAddress.Should().Be("192.168.1.100");
        savedLog.UserAgent.Should().Be("Mozilla/5.0 TestBrowser");
        savedLog.FailureReason.Should().BeNull();
        savedLog.TimestampUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task LogAsync_FailedEvent_PersistsFailureReason()
    {
        // Arrange & Act
        await _service.LogAsync(null, "UserLogin", "unknown@test.com", false, "Invalid email or password.");

        // Assert
        var savedLog = await _dbContext.AuditLogs.FirstOrDefaultAsync(a => a.Action == "UserLogin" && !a.IsSuccess);
        savedLog.Should().NotBeNull();
        savedLog!.UserId.Should().BeNull();
        savedLog.ResourceId.Should().Be("unknown@test.com");
        savedLog.IsSuccess.Should().BeFalse();
        savedLog.FailureReason.Should().Be("Invalid email or password.");
    }
}
