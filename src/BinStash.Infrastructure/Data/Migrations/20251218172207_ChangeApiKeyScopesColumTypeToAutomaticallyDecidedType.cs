using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BinStash.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ChangeApiKeyScopesColumTypeToAutomaticallyDecidedType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Scopes",
                table: "ApiKeys");
            migrationBuilder.AddColumn<string[]>(
                name: "Scopes",
                table: "ApiKeys",
                type: "text[]",
                nullable: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Scopes",
                table: "ApiKeys");
            migrationBuilder.AddColumn<string[]>(
                name: "Scopes",
                table: "ApiKeys",
                type: "jsonb",
                nullable: false);
        }
    }
}
