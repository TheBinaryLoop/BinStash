using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BinStash.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropFileDefinitionStorageKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FileDefinitions_ChunkStoreId_StorageKey",
                table: "FileDefinitions");

            migrationBuilder.DropColumn(
                name: "StorageKey",
                table: "FileDefinitions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "StorageKey",
                table: "FileDefinitions",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateIndex(
                name: "IX_FileDefinitions_ChunkStoreId_StorageKey",
                table: "FileDefinitions",
                columns: new[] { "ChunkStoreId", "StorageKey" },
                unique: true,
                filter: "\"StorageKey\" != '\\x'::bytea");
        }
    }
}
