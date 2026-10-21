namespace PayFlow.Application.Common.Interfaces;

public interface IPayFlowMetrics
{
    void RecordTransfer(string status, string currency, double durationSeconds);
    void SetActiveWalletsCount(long count);
    long GetActiveWalletsCount();
}
