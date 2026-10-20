using PayFlow.Application.DTOs;

namespace PayFlow.Application.Common.Interfaces;

public interface IDeadLetterQueueService
{
    Task<IReadOnlyList<DeadLetterMessageDto>> GetDeadLetterMessagesAsync(int count = 20, CancellationToken cancellationToken = default);
    Task<DlqRedeliveryResult> RedeliverDeadLetterMessagesAsync(int count = 10, CancellationToken cancellationToken = default);
    Task<uint> PurgeDeadLetterQueueAsync(CancellationToken cancellationToken = default);
}
