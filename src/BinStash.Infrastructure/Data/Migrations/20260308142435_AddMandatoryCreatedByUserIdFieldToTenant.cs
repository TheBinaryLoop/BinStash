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
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

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
