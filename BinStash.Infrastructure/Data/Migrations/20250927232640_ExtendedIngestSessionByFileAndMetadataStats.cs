using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BinStash.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExtendedIngestSessionByFileAndMetadataStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FilesSeenNew",
                table: "IngestSessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FilesSeenTotal",
                table: "IngestSessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FilesSeenUnique",
                table: "IngestSessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MetadataSize",
                table: "IngestSessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FilesSeenNew",
                table: "IngestSessions");

            migrationBuilder.DropColumn(
                name: "FilesSeenTotal",
                table: "IngestSessions");

            migrationBuilder.DropColumn(
                name: "FilesSeenUnique",
                table: "IngestSessions");

            migrationBuilder.DropColumn(
                name: "MetadataSize",
                table: "IngestSessions");
        }
    }
}
