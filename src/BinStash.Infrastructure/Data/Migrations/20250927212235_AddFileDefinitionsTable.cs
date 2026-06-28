using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BinStash.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFileDefinitionsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FileDefinitions",
                columns: table => new
                {
                    Checksum = table.Column<byte[]>(type: "bytea", nullable: false),
                    ChunkStoreId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileDefinitions", x => new { x.ChunkStoreId, x.Checksum });
                    table.CheckConstraint("chk_file_definitions_checksum_len", "octet_length(\"Checksum\") = 32");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FileDefinitions");
        }
    }
}
