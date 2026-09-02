using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PayFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSystemClearingAndInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxMessages", x => x.Id);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Wallets_Balance_NonNegative",
                table: "Wallets",
                sql: "\"Balance\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transactions_Amount_Positive",
                table: "Transactions",
                sql: "\"Amount\" > 0");

            migrationBuilder.CreateIndex(
                name: "IX_InboxMessages_MessageId",
                table: "InboxMessages",
                column: "MessageId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InboxMessages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Wallets_Balance_NonNegative",
                table: "Wallets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Transactions_Amount_Positive",
                table: "Transactions");
        }
    }
}
