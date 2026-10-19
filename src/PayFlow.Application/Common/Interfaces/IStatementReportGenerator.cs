using PayFlow.Application.DTOs;

namespace PayFlow.Application.Common.Interfaces;

public interface IStatementReportGenerator
{
    byte[] GenerateCsv(StatementSummaryDto statement);
    byte[] GeneratePdf(StatementSummaryDto statement);
}
