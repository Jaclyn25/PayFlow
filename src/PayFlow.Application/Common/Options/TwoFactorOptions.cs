namespace PayFlow.Application.Common.Options;

public class TwoFactorOptions
{
    public const string SectionName = "TwoFactorSettings";

    public string Issuer { get; set; } = "PayFlow";
    public int ClockDriftToleranceSteps { get; set; } = 1; // +/- 30s
    public int StepSizeSeconds { get; set; } = 30;
    public int Digits { get; set; } = 6;
    public int RecoveryCodesCount { get; set; } = 10;
    public decimal StepUpTransferThreshold { get; set; } = 5000.00m;
    public bool RequireStepUpForTransfers { get; set; } = true;
}
