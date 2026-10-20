namespace PayFlow.Application.DTOs;

public record DeadLetterMessageDto(
    string MessageId,
    string Payload,
    string OriginalRoutingKey,
    string? DeathReason,
    int DeathCount,
    DateTime? DeadLetteredAtUtc,
    IReadOnlyDictionary<string, string>? Headers
);

public record DlqRedeliveryResult(
    int RedeliveredCount,
    string Message
);

public record DlqPurgeResult(
    uint PurgedCount,
    string Message
);
