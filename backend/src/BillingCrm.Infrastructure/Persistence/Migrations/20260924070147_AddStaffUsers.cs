using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillingCrm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StaffUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffUsers", x => x.Id);
                    table.CheckConstraint("CK_StaffUsers_Role", "[Role] IN ('Admin', 'Agent')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_StaffUsers_NormalizedEmail",
                table: "StaffUsers",
                column: "NormalizedEmail",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StaffUsers");
        }
    }
}
