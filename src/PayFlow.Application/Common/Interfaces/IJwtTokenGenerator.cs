using PayFlow.Domain.Entities;

namespace PayFlow.Application.Common.Interfaces;

public interface IJwtTokenGenerator
{
    (string AccessToken, string RawRefreshToken, DateTime RefreshTokenExpiresAtUtc) GenerateTokens(User user);
    string HashToken(string rawToken);
    string GenerateTwoFactorChallengeToken(User user);
    (bool IsValid, Guid UserId) ValidateTwoFactorChallengeToken(string token);
}

