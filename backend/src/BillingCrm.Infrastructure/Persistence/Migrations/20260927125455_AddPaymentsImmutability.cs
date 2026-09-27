using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillingCrm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentsImmutability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Payments are permanent (spec 008): no edit, void or delete has ever existed for one. This closes the gap below the
            // application layer, the exact shape of spec 010's TR_InvoiceHistoryEntries_Immutable applied to a different table. An
            // INSTEAD OF trigger replaces the update or delete with this error, so nothing happens to the row and the statement
            // fails. Inserts (recording a payment) are untouched. Hand-written: EF Core cannot generate a trigger.
            migrationBuilder.Sql(
                """
                CREATE TRIGGER [dbo].[TR_Payments_Immutable]
                ON [dbo].[Payments]
                INSTEAD OF UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51000, 'Payments cannot be changed or deleted.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The trigger goes first: with it in place nothing could remove the table's rows. Rolling back the migration removes the
            // immutability guarantee on purpose; it is the developer's own decision to undo this feature, never something the
            // application can do.
            migrationBuilder.Sql("DROP TRIGGER [dbo].[TR_Payments_Immutable];");
        }
    }
}
