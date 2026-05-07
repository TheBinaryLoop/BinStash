using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BinStash.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class MakeApiKeysLinkableToMoreSubjectTypesAndFixDataTypeOfDateTimeFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ApiKeys_ServiceAccounts_ServiceAccountId",
                table: "ApiKeys");

            migrationBuilder.AlterColumn<Guid>(
                name: "ServiceAccountId",
                table: "ApiKeys",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "SubjectId",
                table: "ApiKeys",
                type: "uuid",
                nullable: false);

            migrationBuilder.AddColumn<short>(
                name: "SubjectType",
                table: "ApiKeys",
                type: "smallint",
                nullable: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SubjectId",
                table: "ApiKeys");

            migrationBuilder.DropColumn(
                name: "SubjectType",
                table: "ApiKeys");

            migrationBuilder.AlterColumn<Guid>(
                name: "ServiceAccountId",
                table: "ApiKeys",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ApiKeys_ServiceAccounts_ServiceAccountId",
                table: "ApiKeys",
                column: "ServiceAccountId",
                principalTable: "ServiceAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
