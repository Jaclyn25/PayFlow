using System.Globalization;
using System.Text;
using PayFlow.Application.DTOs;

namespace PayFlow.Infrastructure.Reporting;

public static class CsvStatementGenerator
{
    public static byte[] Generate(StatementSummaryDto statement)
    {
        var sb = new StringBuilder();

        // 1. Metadata / Reconciliation Header
        sb.AppendLine("PayFlow Official Account Statement");
        sb.AppendLine($"Account Holder,{EscapeCsv(statement.UserName)}");
        sb.AppendLine($"Email,{EscapeCsv(statement.UserEmail)}");
        sb.AppendLine($"Wallet ID,{statement.WalletId}");
        sb.AppendLine($"Currency,{statement.Currency}");
        sb.AppendLine($"Period From,{statement.FromDate:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"Period To,{statement.ToDate:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"Generated At,{statement.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"Verification Hash,{statement.VerificationHash}");
        sb.AppendLine();

        // 2. Reconciliation Summary
        sb.AppendLine("Reconciliation Summary");
        sb.AppendLine($"Opening Balance,{statement.OpeningBalance.ToString("F2", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"Total Credits (+),{statement.TotalCredits.ToString("F2", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"Total Debits (-),{statement.TotalDebits.ToString("F2", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"Net Movement,{statement.NetMovement.ToString("F2", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"Closing Balance (=),{statement.ClosingBalance.ToString("F2", CultureInfo.InvariantCulture)}");
        sb.AppendLine();

        // 3. Transactions Table (RFC 4180 Table)
        sb.AppendLine("Date (UTC),Reference,Type,Description,Debit,Credit,Balance After,Currency");

        foreach (var entry in statement.Entries)
        {
            var dateStr = entry.Date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            var refStr = EscapeCsv(entry.Reference);
            var typeStr = EscapeCsv(entry.Type);
            var descStr = EscapeCsv(entry.Description);
            var debitStr = entry.Debit.HasValue ? entry.Debit.Value.ToString("F2", CultureInfo.InvariantCulture) : "";
            var creditStr = entry.Credit.HasValue ? entry.Credit.Value.ToString("F2", CultureInfo.InvariantCulture) : "";
            var balanceStr = entry.BalanceAfter.ToString("F2", CultureInfo.InvariantCulture);
            var currStr = EscapeCsv(entry.Currency);

            sb.AppendLine($"{dateStr},{refStr},{typeStr},{descStr},{debitStr},{creditStr},{balanceStr},{currStr}");
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }
}
