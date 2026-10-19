namespace PayFlow.Application.DTOs;

public record RegisterRequest(
    string FirstName,
    string LastName,
    string Email,
    string Password,
    string Currency = "EGP"
);

public record LoginRequest(
    string Email,
    string Password
);

public record RefreshTokenRequest(
    string RefreshToken
);

public record RevokeTokenRequest(
    string RefreshToken
);

public record AuthResponse(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAtUtc,
    UserProfileResponse? User,
    bool RequiresTwoFactor = false,
    string? TwoFactorToken = null,
    string? Message = null
);

public record UserProfileResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    bool IsActive,
    DateTime CreatedAtUtc,
    string KycTier = "Tier1",
    bool TwoFactorEnabled = false,
    string Role = "Customer",
    bool EmailConfirmed = false
);

public record ForgotPasswordRequest(
    string Email
);

public record ResetPasswordRequest(
    string Token,
    string NewPassword
);

public record VerifyEmailRequest(
    string Token
);

public record MessageResponse(
    string Message
);


