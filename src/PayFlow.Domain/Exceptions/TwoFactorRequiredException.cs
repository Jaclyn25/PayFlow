namespace PayFlow.Domain.Exceptions;

public class TwoFactorRequiredException : Exception
{
    public decimal ThresholdAmount { get; }
    public decimal AttemptedAmount { get; }

    public TwoFactorRequiredException(string message) : base(message)
    {
    }

    public TwoFactorRequiredException(decimal thresholdAmount, decimal attemptedAmount)
        : base($"Two-factor authentication code is required for transfers of {thresholdAmount:N2} or more.")
    {
        ThresholdAmount = thresholdAmount;
        AttemptedAmount = attemptedAmount;
    }
}
