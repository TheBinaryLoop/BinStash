using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BinStash.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMandatoryCreatedByUserIdFieldToTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "Tenants",
                type: "uuid",
                // Nullable, with no default.
                //
                // This was NOT NULL defaulting to Guid.Empty, which only worked while Tenants
                // was empty: on any instance that already had a tenant, every row got
                // Guid.Empty and the foreign key below then failed with
                // "insert or update on table \"Tenants\" violates foreign key constraint
                // FK_Tenants_AspNetUsers_CreatedByUserId" — there is no user with that id, and
                // on an instance predating auth there are no users at all to point at.
                //
                // NULL is also the honest value: for a tenant that existed before this column,
                // we genuinely do not know who created it. Migration
                // 20260628052119_MakeTenantCreatedByUserIdNullable later relaxes this column to
                // nullable anyway, so this only brings the end state forward; that migration
                // becomes a no-op for the column itself.
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_CreatedByUserId",
                table: "Tenants",
                column: "CreatedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Tenants_AspNetUsers_CreatedByUserId",
                table: "Tenants",
                column: "CreatedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tenants_AspNetUsers_CreatedByUserId",
                table: "Tenants");

            migrationBuilder.DropIndex(
                name: "IX_Tenants_CreatedByUserId",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Tenants");
        }
    }
}
