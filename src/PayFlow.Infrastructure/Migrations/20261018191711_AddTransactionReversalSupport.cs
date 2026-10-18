using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PayFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionReversalSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OriginalTransactionId",
                table: "Transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReversalReason",
                table: "Transactions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReversalTransactionId",
                table: "Transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_OriginalTransactionId",
                table: "Transactions",
                column: "OriginalTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_ReversalTransactionId",
                table: "Transactions",
                column: "ReversalTransactionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Transactions_OriginalTransactionId",
                table: "Transactions",
                column: "OriginalTransactionId",
                principalTable: "Transactions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Transactions_ReversalTransactionId",
                table: "Transactions",
                column: "ReversalTransactionId",
                principalTable: "Transactions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Transactions_OriginalTransactionId",
                table: "Transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Transactions_ReversalTransactionId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_OriginalTransactionId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_ReversalTransactionId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "OriginalTransactionId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ReversalReason",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ReversalTransactionId",
                table: "Transactions");
        }
    }
}
