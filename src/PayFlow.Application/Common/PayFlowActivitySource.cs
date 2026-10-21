using System.Diagnostics;

namespace PayFlow.Application.Common;

public static class PayFlowActivitySource
{
    public const string ActivitySourceName = "PayFlow";
    public const string MeterName = "PayFlow.Metrics";

    public static readonly ActivitySource Instance = new(ActivitySourceName, "1.0.0");
}
