using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using PayFlow.Application.DTOs;

namespace PayFlow.Infrastructure.Reporting;

public static class PdfStatementGenerator
{
    static PdfStatementGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.UseSystemFonts = true;
    }

    public static byte[] Generate(StatementSummaryDto statement)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Lato").FontColor(Colors.Grey.Darken4));

                page.Header().Element(header => ComposeHeader(header, statement));
                page.Content().Element(content => ComposeContent(content, statement));
                page.Footer().Element(footer => ComposeFooter(footer, statement));
            });
        });

        return document.GeneratePdf();
    }

    private static void ComposeHeader(IContainer container, StatementSummaryDto statement)
    {
        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().Text("PAYFLOW").FontSize(20).Bold().FontColor(Colors.Blue.Darken3);
                    col.Item().Text("Official Account Statement").FontSize(12).SemiBold().FontColor(Colors.Grey.Darken2);
                });

                row.RelativeItem().Column(col =>
                {
                    col.Item().AlignRight().Text($"Statement Period:").SemiBold().FontSize(8);
                    col.Item().AlignRight().Text($"{statement.FromDate:yyyy-MM-dd} to {statement.ToDate:yyyy-MM-dd} UTC").FontSize(8);
                    col.Item().AlignRight().Text($"Generated: {statement.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC").FontSize(7).FontColor(Colors.Grey.Medium);
                });
            });

            column.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
        });
    }

    private static void ComposeContent(IContainer container, StatementSummaryDto statement)
    {
        container.PaddingVertical(10).Column(column =>
        {
            // 1. Account Details Cards
            column.Item().Row(row =>
            {
                row.RelativeItem().Background(Colors.Grey.Lighten4).Padding(8).Column(col =>
                {
                    col.Item().Text("ACCOUNT HOLDER DETAILS").FontSize(8).Bold().FontColor(Colors.Blue.Darken2);
                    col.Item().Text($"Name: {statement.UserName}").FontSize(8);
                    col.Item().Text($"Email: {statement.UserEmail}").FontSize(8);
                    col.Item().Text($"Wallet ID: {statement.WalletId}").FontSize(7).FontColor(Colors.Grey.Darken1);
                });

                row.ConstantItem(10);

                row.RelativeItem().Background(Colors.Grey.Lighten4).Padding(8).Column(col =>
                {
                    col.Item().Text("RECONCILIATION SUMMARY").FontSize(8).Bold().FontColor(Colors.Blue.Darken2);
                    col.Item().Text($"Opening Balance: {statement.OpeningBalance:N2} {statement.Currency}").FontSize(8);
                    col.Item().Text($"Total Credits (+): {statement.TotalCredits:N2} {statement.Currency}").FontSize(8).FontColor(Colors.Green.Darken2);
                    col.Item().Text($"Total Debits (-): {statement.TotalDebits:N2} {statement.Currency}").FontSize(8).FontColor(Colors.Red.Darken2);
                    col.Item().Text($"Closing Balance (=): {statement.ClosingBalance:N2} {statement.Currency}").FontSize(8).Bold();
                });
            });

            // 2. Transactions Table
            column.Item().PaddingTop(15).Text($"TRANSACTIONS ({statement.TransactionCount})").FontSize(10).Bold().FontColor(Colors.Grey.Darken3);

            column.Item().PaddingTop(5).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(75);  // Date
                    columns.ConstantColumn(85);  // Reference
                    columns.ConstantColumn(55);  // Type
                    columns.RelativeColumn();    // Description
                    columns.ConstantColumn(60);  // Debit
                    columns.ConstantColumn(60);  // Credit
                    columns.ConstantColumn(65);  // Balance
                });

                table.Header(header =>
                {
                    header.Cell().Background(Colors.Blue.Darken3).Padding(4).Text("Date (UTC)").Bold().FontColor(Colors.White).FontSize(8);
                    header.Cell().Background(Colors.Blue.Darken3).Padding(4).Text("Reference").Bold().FontColor(Colors.White).FontSize(8);
                    header.Cell().Background(Colors.Blue.Darken3).Padding(4).Text("Type").Bold().FontColor(Colors.White).FontSize(8);
                    header.Cell().Background(Colors.Blue.Darken3).Padding(4).Text("Description").Bold().FontColor(Colors.White).FontSize(8);
                    header.Cell().Background(Colors.Blue.Darken3).Padding(4).AlignRight().Text("Debit").Bold().FontColor(Colors.White).FontSize(8);
                    header.Cell().Background(Colors.Blue.Darken3).Padding(4).AlignRight().Text("Credit").Bold().FontColor(Colors.White).FontSize(8);
                    header.Cell().Background(Colors.Blue.Darken3).Padding(4).AlignRight().Text("Balance").Bold().FontColor(Colors.White).FontSize(8);
                });

                for (var i = 0; i < statement.Entries.Count; i++)
                {
                    var entry = statement.Entries[i];
                    var background = i % 2 == 0 ? Colors.White : Colors.Grey.Lighten5;

                    table.Cell().Background(background).Padding(3).Text(entry.Date.ToString("yyyy-MM-dd HH:mm")).FontSize(7);
                    table.Cell().Background(background).Padding(3).Text(entry.Reference).FontSize(7);
                    table.Cell().Background(background).Padding(3).Text(entry.Type).FontSize(7);
                    table.Cell().Background(background).Padding(3).Text(entry.Description).FontSize(7);
                    table.Cell().Background(background).Padding(3).AlignRight().Text(entry.Debit.HasValue ? $"{entry.Debit.Value:N2}" : "-").FontColor(Colors.Red.Darken2).FontSize(7);
                    table.Cell().Background(background).Padding(3).AlignRight().Text(entry.Credit.HasValue ? $"{entry.Credit.Value:N2}" : "-").FontColor(Colors.Green.Darken2).FontSize(7);
                    table.Cell().Background(background).Padding(3).AlignRight().Text($"{entry.BalanceAfter:N2}").SemiBold().FontSize(7);
                }
            });
        });
    }

    private static void ComposeFooter(IContainer container, StatementSummaryDto statement)
    {
        container.Column(col =>
        {
            col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
            col.Item().PaddingTop(5).Row(row =>
            {
                row.RelativeItem().Column(sub =>
                {
                    sub.Item().Text($"Verification Hash: {statement.VerificationHash}").FontSize(6).FontColor(Colors.Grey.Darken1);
                    sub.Item().Text("This document is an authentic electronic record issued by PayFlow. Zero-Sum Invariant Checked.").FontSize(6).FontColor(Colors.Grey.Medium);
                });

                row.RelativeItem().AlignRight().Text(text =>
                {
                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                });
            });
        });
    }
}
