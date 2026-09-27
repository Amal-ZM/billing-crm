using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillingCrm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductUnitPriceUpperBound : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_UnitPrice",
                table: "Products");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Products_UnitPrice",
                table: "Products",
                sql: "[UnitPrice] >= 0 AND [UnitPrice] <= 999999999.99");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_UnitPrice",
                table: "Products");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Products_UnitPrice",
                table: "Products",
                sql: "[UnitPrice] >= 0");
        }
    }
}
