using PayFlow.Domain.Enums;

namespace PayFlow.Domain.Entities;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public UserRole Role { get; set; } = UserRole.Customer;
    public KycTier KycTier { get; set; } = KycTier.Tier1;
    public DateTime? KycVerifiedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public Wallet? Wallet { get; set; }
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();

    public bool EmailConfirmed { get; set; } = false;
    public string? EmailVerificationTokenHash { get; set; }
    public DateTime? EmailVerificationTokenExpiresAtUtc { get; set; }
    public string? PasswordResetTokenHash { get; set; }
    public DateTime? PasswordResetTokenExpiresAtUtc { get; set; }

    public bool TwoFactorEnabled { get; set; } = false;
    public string? TwoFactorSecret { get; set; }
    public DateTime? TwoFactorEnabledAtUtc { get; set; }
    public ICollection<TwoFactorRecoveryCode> TwoFactorRecoveryCodes { get; set; } = new List<TwoFactorRecoveryCode>();
}

