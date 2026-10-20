namespace PayFlow.Application.Common.Interfaces;

public interface INotificationMessageProcessor
{
    Task ProcessAsync(string messagePayload, CancellationToken cancellationToken = default);
}
