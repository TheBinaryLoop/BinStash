using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BinStash.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSerializerVersionToReleaseTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "SerializerVersion",
                table: "Releases",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SerializerVersion",
                table: "Releases");
        }
    }
}
