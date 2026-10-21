using System.Security.Cryptography;
using System.Text;
using PayFlow.Application.Common.Interfaces;

namespace PayFlow.Infrastructure.Security;

public class WebhookSigner : IWebhookSigner
{
    public string ComputeSignature(string payload, string secretKey)
    {
        if (payload == null)
        {
            throw new ArgumentNullException(nameof(payload));
        }

        if (string.IsNullOrEmpty(secretKey))
        {
            throw new ArgumentException("Secret key cannot be null or empty.", nameof(secretKey));
        }

        var keyBytes = Encoding.UTF8.GetBytes(secretKey);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);

        using var hmac = new HMACSHA256(keyBytes);
        var hashBytes = hmac.ComputeHash(payloadBytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    public bool VerifySignature(string payload, string secretKey, string signature)
    {
        if (payload == null || string.IsNullOrWhiteSpace(secretKey) || string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        var cleanSignature = signature.Trim();
        if (cleanSignature.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
        {
            cleanSignature = cleanSignature.Substring(7).Trim();
        }
        else if (cleanSignature.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            cleanSignature = cleanSignature.Substring(7).Trim();
        }

        var expectedSignature = ComputeSignature(payload, secretKey);

        var expectedBytes = Encoding.UTF8.GetBytes(expectedSignature);
        var actualBytes = Encoding.UTF8.GetBytes(cleanSignature.ToLowerInvariant());

        return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}
