using System.Diagnostics;
using PayFlow.Application.Common;

namespace PayFlow.Infrastructure.Observability;

public static class PayFlowDiagnostics
{
    public const string ActivitySourceName = PayFlowActivitySource.ActivitySourceName;
    public const string MeterName = PayFlowActivitySource.MeterName;

    public static ActivitySource ActivitySource => PayFlowActivitySource.Instance;
}
