using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PayFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiCurrencyAndExchangeRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRate",
                table: "Transactions",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TargetAmount",
                table: "Transactions",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetCurrency",
                table: "Transactions",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "LedgerEntries",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "TargetAmount",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "TargetCurrency",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "LedgerEntries");
        }
    }
}
