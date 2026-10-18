using Microsoft.Extensions.Logging;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Domain.Entities;

namespace PayFlow.Infrastructure.Services;

public class AuditLogService : IAuditLogService
{
    private readonly IPayFlowDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(
        IPayFlowDbContext dbContext,
        ICurrentUserService currentUserService,
        ILogger<AuditLogService> logger)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task LogAsync(
        Guid? userId,
        string action,
        string? resourceId,
        bool isSuccess,
        string? failureReason = null,
        CancellationToken cancellationToken = default)
    {
        var ip = _currentUserService.IpAddress ?? "Unknown";
        var userAgent = _currentUserService.UserAgent;
        var effectiveUserId = userId ?? _currentUserService.UserId;

        var auditLog = new AuditLog
        {
            UserId = effectiveUserId,
            Action = action,
            ResourceId = resourceId,
            IpAddress = ip,
            UserAgent = userAgent,
            IsSuccess = isSuccess,
            FailureReason = failureReason,
            TimestampUtc = DateTime.UtcNow
        };

        _dbContext.AuditLogs.Add(auditLog);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Security Audit: Action={Action}, UserId={UserId}, ResourceId={ResourceId}, Success={IsSuccess}, IP={IpAddress}, Reason={FailureReason}",
            action, effectiveUserId, resourceId, isSuccess, ip, failureReason);
    }
}
