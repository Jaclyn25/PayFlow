using FluentAssertions;
using PayFlow.Infrastructure.Security;

namespace PayFlow.UnitTests.Services;

public class WebhookSignerTests
{
    private readonly WebhookSigner _signer = new();

    [Fact]
    public void ComputeSignature_WithValidPayloadAndKey_ReturnsExpectedHmacSha256Hex()
    {
        // Arrange
        var payload = "{\"test\":123}";
        var secretKey = "whsec_supersecretkey123";

        // Act
        var sig1 = _signer.ComputeSignature(payload, secretKey);
        var sig2 = _signer.ComputeSignature(payload, secretKey);

        // Assert
        sig1.Should().NotBeNullOrWhiteSpace();
        sig1.Should().Be(sig2, "HMAC-SHA256 must be deterministic");
        sig1.Length.Should().Be(64, "SHA-256 hex string is 64 characters long");
    }

    [Fact]
    public void ComputeSignature_WithNullPayload_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => _signer.ComputeSignature(null!, "key");
        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void ComputeSignature_WithEmptyOrNullKey_ThrowsArgumentException(string? key)
    {
        // Act & Assert
        var act = () => _signer.ComputeSignature("payload", key!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void VerifySignature_WithCorrectSignature_ReturnsTrue()
    {
        // Arrange
        var payload = "{\"event\":\"transfer.completed\",\"amount\":500}";
        var secretKey = "whsec_testkey999";
        var computedSig = _signer.ComputeSignature(payload, secretKey);

        // Act
        var isValidRaw = _signer.VerifySignature(payload, secretKey, computedSig);
        var isValidPrefixed = _signer.VerifySignature(payload, secretKey, $"sha256={computedSig}");
        var isValidPrefixedColon = _signer.VerifySignature(payload, secretKey, $"sha256:{computedSig}");

        // Assert
        isValidRaw.Should().BeTrue();
        isValidPrefixed.Should().BeTrue();
        isValidPrefixedColon.Should().BeTrue();
    }

    [Fact]
    public void VerifySignature_WithTamperedPayload_ReturnsFalse()
    {
        // Arrange
        var originalPayload = "{\"amount\":100}";
        var tamperedPayload = "{\"amount\":1000}";
        var secretKey = "whsec_secret";
        var signature = _signer.ComputeSignature(originalPayload, secretKey);

        // Act
        var isValid = _signer.VerifySignature(tamperedPayload, secretKey, signature);

        // Assert
        isValid.Should().BeFalse();
    }

    [Fact]
    public void VerifySignature_WithIncorrectSecret_ReturnsFalse()
    {
        // Arrange
        var payload = "{\"amount\":100}";
        var signature = _signer.ComputeSignature(payload, "key-one");

        // Act
        var isValid = _signer.VerifySignature(payload, "key-two", signature);

        // Assert
        isValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("", "key", "sig")]
    [InlineData("payload", "", "sig")]
    [InlineData("payload", "key", "")]
    [InlineData("payload", "key", "invalid-sig-format")]
    public void VerifySignature_WithInvalidInputs_ReturnsFalse(string payload, string key, string sig)
    {
        // Act
        var isValid = _signer.VerifySignature(payload, key, sig);

        // Assert
        isValid.Should().BeFalse();
    }
}
