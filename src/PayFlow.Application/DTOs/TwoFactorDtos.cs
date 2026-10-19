namespace PayFlow.Application.DTOs;

public record TwoFactorSetupResponse(
    string SharedKey,
    string AuthenticatorUri,
    string QrCodeDataUri,
    IReadOnlyList<string> RecoveryCodes
);

public record EnableTwoFactorRequest(
    string Code
);

public record DisableTwoFactorRequest(
    string Code
);

public record VerifyTwoFactorRequest(
    string TwoFactorToken,
    string Code
);

public record TwoFactorStatusResponse(
    bool IsEnabled,
    DateTime? EnabledAtUtc
);
