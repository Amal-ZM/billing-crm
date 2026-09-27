using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillingCrm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InvoiceHistoryEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActorEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ActorRole = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ChangesJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceHistoryEntries", x => x.Id);
                    table.CheckConstraint("CK_InvoiceHistoryEntries_ActorRole", "[ActorRole] IN ('Admin','Agent')");
                    table.CheckConstraint("CK_InvoiceHistoryEntries_ChangesJson", "ISJSON([ChangesJson]) = 1");
                    table.CheckConstraint("CK_InvoiceHistoryEntries_Kind", "[Kind] IN ('Created','DraftEdited','Sent','Payment','Cancelled')");
                    table.ForeignKey(
                        name: "FK_InvoiceHistoryEntries_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceHistoryEntries_InvoiceId_Sequence",
                table: "InvoiceHistoryEntries",
                columns: new[] { "InvoiceId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceHistoryEntries_Sequence",
                table: "InvoiceHistoryEntries",
                column: "Sequence",
                unique: true);

            // History is permanent (spec 010): whatever any code or query tries, no entry is ever changed or removed. An INSTEAD OF trigger
            // replaces the update or delete with this error, so nothing happens to the row and the statement fails. Inserts are untouched.
            // Hand-written: EF Core cannot generate a trigger. Dropping the trigger needs database-administrator rights (a known limit).
            migrationBuilder.Sql(
                """
                CREATE TRIGGER [dbo].[TR_InvoiceHistoryEntries_Immutable]
                ON [dbo].[InvoiceHistoryEntries]
                INSTEAD OF UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51000, 'Invoice history entries cannot be changed or deleted.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The trigger goes first: with it in place nothing could remove the table's rows. Rolling back the migration removes the history
            // on purpose; it is the developer's own decision to undo this feature, never something the application can do.
            migrationBuilder.Sql("DROP TRIGGER [dbo].[TR_InvoiceHistoryEntries_Immutable];");

            migrationBuilder.DropTable(
                name: "InvoiceHistoryEntries");
        }
    }
}
