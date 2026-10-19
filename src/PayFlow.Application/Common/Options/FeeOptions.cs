namespace PayFlow.Application.Common.Options;

public class FeeOptions
{
    public const string SectionName = "FeeSettings";

    public bool Enabled { get; set; } = true;
    public decimal Percentage { get; set; } = 0.01m; // 1%
    public decimal FixedFee { get; set; } = 0.30m;   // 0.30 fixed fee
    public decimal? CapAmount { get; set; } = 50.00m; // Max fee cap
    public decimal MinFee { get; set; } = 0.00m;
}
