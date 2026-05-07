using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BinStash.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProbeTypeToChunkStoreTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "MinFreeBytes",
                table: "ChunkStores",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProbeMode",
                table: "ChunkStores",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MinFreeBytes",
                table: "ChunkStores");

            migrationBuilder.DropColumn(
                name: "ProbeMode",
                table: "ChunkStores");
        }
    }
}
