using PayFlow.Application.Common.Interfaces;
using PayFlow.Application.DTOs;

namespace PayFlow.Infrastructure.Reporting;

public class StatementReportGenerator : IStatementReportGenerator
{
    public byte[] GenerateCsv(StatementSummaryDto statement)
    {
        return CsvStatementGenerator.Generate(statement);
    }

    public byte[] GeneratePdf(StatementSummaryDto statement)
    {
        return PdfStatementGenerator.Generate(statement);
    }
}
