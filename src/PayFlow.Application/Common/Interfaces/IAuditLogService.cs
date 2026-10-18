namespace PayFlow.Application.Common.Interfaces;

public interface IAuditLogService
{
    Task LogAsync(
        Guid? userId,
        string action,
        string? resourceId,
        bool isSuccess,
        string? failureReason = null,
        CancellationToken cancellationToken = default);
}
