using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillingCrm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceStatuses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Invoices_Status",
                table: "Invoices");

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAtUtc",
                table: "Invoices",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FrozenGrandTotal",
                table: "Invoices",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FrozenNetAfterDiscount",
                table: "Invoices",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FrozenTotalNet",
                table: "Invoices",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FrozenTotalVat",
                table: "Invoices",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Invoices",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<DateTime>(
                name: "SentAtUtc",
                table: "Invoices",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FrozenDiscountShare",
                table: "InvoiceLines",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FrozenNet",
                table: "InvoiceLines",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FrozenVat",
                table: "InvoiceLines",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invoices_FrozenAmounts",
                table: "Invoices",
                sql: "([Status] = 'Draft' AND [FrozenTotalNet] IS NULL AND [FrozenNetAfterDiscount] IS NULL AND [FrozenTotalVat] IS NULL AND [FrozenGrandTotal] IS NULL) OR ([Status] <> 'Draft' AND [FrozenTotalNet] IS NOT NULL AND [FrozenNetAfterDiscount] IS NOT NULL AND [FrozenTotalVat] IS NOT NULL AND [FrozenGrandTotal] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invoices_Status",
                table: "Invoices",
                sql: "[Status] IN ('Draft','Sent','Paid','Cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invoices_StatusTimes",
                table: "Invoices",
                sql: "([Status] = 'Draft' AND [SentAtUtc] IS NULL AND [CancelledAtUtc] IS NULL) OR ([Status] IN ('Sent','Paid') AND [SentAtUtc] IS NOT NULL AND [CancelledAtUtc] IS NULL) OR ([Status] = 'Cancelled' AND [CancelledAtUtc] IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Invoices_FrozenAmounts",
                table: "Invoices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Invoices_Status",
                table: "Invoices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Invoices_StatusTimes",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "FrozenGrandTotal",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "FrozenNetAfterDiscount",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "FrozenTotalNet",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "FrozenTotalVat",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "SentAtUtc",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "FrozenDiscountShare",
                table: "InvoiceLines");

            migrationBuilder.DropColumn(
                name: "FrozenNet",
                table: "InvoiceLines");

            migrationBuilder.DropColumn(
                name: "FrozenVat",
                table: "InvoiceLines");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invoices_Status",
                table: "Invoices",
                sql: "[Status] IN ('Draft')");
        }
    }
}
