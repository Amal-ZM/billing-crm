using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillingCrm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NoCacheInvoiceNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EF cannot express NO CACHE. By default SQL Server hands out sequence numbers from a cache, and an unclean stop
            // of the server (a LocalDB restart, a crash) throws the unused cached numbers away, leaving a visible gap in the
            // invoice numbers. NO CACHE writes every number to disk as it is given out, so a restart cannot skip any.
            // A rolled-back save can still leave a gap (a sequence never gives a number back); that is accepted and documented.
            migrationBuilder.Sql("ALTER SEQUENCE [InvoiceNumbers] NO CACHE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER SEQUENCE [InvoiceNumbers] CACHE;");
        }
    }
}
