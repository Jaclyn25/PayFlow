using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PayFlow.Api.Hubs;
using PayFlow.Api.Services;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Application.DTOs;
using Xunit;

namespace PayFlow.UnitTests.Realtime;

public class SignalRRealtimeTests
{
    [Fact]
    public async Task SignalRNotificationService_NotifyBalanceUpdatedAsync_BroadcastsToTargetUserGroupAndUser()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var userGroup = WalletHub.GetUserGroupName(userId);
        var notification = new BalanceUpdatedNotification(Guid.NewGuid(), userId, 1500m, "EGP", DateTime.UtcNow);

        var hubContextMock = new Mock<IHubContext<WalletHub, IWalletHubClient>>();
        var hubClientsMock = new Mock<IHubClients<IWalletHubClient>>();
        var clientProxyMock = new Mock<IWalletHubClient>();

        hubContextMock.Setup(h => h.Clients).Returns(hubClientsMock.Object);
        hubClientsMock.Setup(c => c.Group(userGroup)).Returns(clientProxyMock.Object);
        hubClientsMock.Setup(c => c.User(userId.ToString())).Returns(clientProxyMock.Object);

        var service = new SignalRNotificationService(hubContextMock.Object, NullLogger<SignalRNotificationService>.Instance);

        // Act
        await service.NotifyBalanceUpdatedAsync(userId, notification);

        // Assert
        clientProxyMock.Verify(c => c.BalanceUpdated(notification), Times.Exactly(2));
    }

    [Fact]
    public async Task SignalRNotificationService_NotifyTransferReceivedAsync_BroadcastsToRecipientGroup()
    {
        // Arrange
        var recipientId = Guid.NewGuid();
        var userGroup = WalletHub.GetUserGroupName(recipientId);
        var notification = new TransferReceivedNotification(Guid.NewGuid(), "TXN-999", Guid.NewGuid(), recipientId, 500m, "EGP", DateTime.UtcNow);

        var hubContextMock = new Mock<IHubContext<WalletHub, IWalletHubClient>>();
        var hubClientsMock = new Mock<IHubClients<IWalletHubClient>>();
        var clientProxyMock = new Mock<IWalletHubClient>();

        hubContextMock.Setup(h => h.Clients).Returns(hubClientsMock.Object);
        hubClientsMock.Setup(c => c.Group(userGroup)).Returns(clientProxyMock.Object);
        hubClientsMock.Setup(c => c.User(recipientId.ToString())).Returns(clientProxyMock.Object);

        var service = new SignalRNotificationService(hubContextMock.Object, NullLogger<SignalRNotificationService>.Instance);

        // Act
        await service.NotifyTransferReceivedAsync(recipientId, notification);

        // Assert
        clientProxyMock.Verify(c => c.TransferReceived(notification), Times.Exactly(2));
    }

    [Fact]
    public async Task SignalRNotificationService_NotifyTransferSentAsync_BroadcastsToSenderGroup()
    {
        // Arrange
        var senderId = Guid.NewGuid();
        var userGroup = WalletHub.GetUserGroupName(senderId);
        var notification = new TransferSentNotification(Guid.NewGuid(), "TXN-888", senderId, Guid.NewGuid(), 300m, 5m, "EGP", DateTime.UtcNow);

        var hubContextMock = new Mock<IHubContext<WalletHub, IWalletHubClient>>();
        var hubClientsMock = new Mock<IHubClients<IWalletHubClient>>();
        var clientProxyMock = new Mock<IWalletHubClient>();

        hubContextMock.Setup(h => h.Clients).Returns(hubClientsMock.Object);
        hubClientsMock.Setup(c => c.Group(userGroup)).Returns(clientProxyMock.Object);
        hubClientsMock.Setup(c => c.User(senderId.ToString())).Returns(clientProxyMock.Object);

        var service = new SignalRNotificationService(hubContextMock.Object, NullLogger<SignalRNotificationService>.Instance);

        // Act
        await service.NotifyTransferSentAsync(senderId, notification);

        // Assert
        clientProxyMock.Verify(c => c.TransferSent(notification), Times.Exactly(2));
    }

    [Fact]
    public async Task WalletHub_Ping_ReturnsPong()
    {
        // Arrange
        var hub = new WalletHub();

        // Act
        var result = await hub.Ping();

        // Assert
        result.Should().Be("pong");
    }

    [Fact]
    public void CustomUserIdProvider_ExtractsUserIdFromClaims()
    {
        // Arrange
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "user-id-456")
        };
        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);

        // Act
        var userId = CustomUserIdProvider.GetUserIdFromPrincipal(principal);

        // Assert
        userId.Should().Be("user-id-456");
    }

    [Fact]
    public void CustomUserIdProvider_ExtractsUserIdFromSubClaim_WhenNameIdentifierMissing()
    {
        // Arrange
        var claims = new[]
        {
            new Claim("sub", "sub-user-789")
        };
        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);

        // Act
        var userId = CustomUserIdProvider.GetUserIdFromPrincipal(principal);

        // Assert
        userId.Should().Be("sub-user-789");
    }

    [Fact]
    public void CustomUserIdProvider_ReturnsNull_WhenPrincipalOrClaimsMissing()
    {
        // Act
        var userId = CustomUserIdProvider.GetUserIdFromPrincipal(null);

        // Assert
        userId.Should().BeNull();
    }
}
