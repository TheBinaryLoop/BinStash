using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BinStash.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChunkStoreGcTombstones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChunkStoreGcTombstones",
                columns: table => new
                {
                    ChunkStoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Checksum = table.Column<byte[]>(type: "bytea", nullable: false),
                    BucketPrefix = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuarantinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EligibleAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PackFileNo = table.Column<int>(type: "integer", nullable: false),
                    PackOffset = table.Column<long>(type: "bigint", nullable: false),
                    PackLength = table.Column<int>(type: "integer", nullable: false),
                    LogicalLength = table.Column<long>(type: "bigint", nullable: false),
                    CompressedLength = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChunkStoreGcTombstones", x => new { x.ChunkStoreId, x.Category, x.Checksum });
                    table.CheckConstraint("chk_gc_tombstones_checksum_len", "octet_length(\"Checksum\") = 32");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChunkStoreGcTombstones_ChunkStoreId_Category_BucketPrefix_E~",
                table: "ChunkStoreGcTombstones",
                columns: new[] { "ChunkStoreId", "Category", "BucketPrefix", "EligibleAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChunkStoreGcTombstones_ChunkStoreId_EligibleAt",
                table: "ChunkStoreGcTombstones",
                columns: new[] { "ChunkStoreId", "EligibleAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChunkStoreGcTombstones_RunId",
                table: "ChunkStoreGcTombstones",
                column: "RunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChunkStoreGcTombstones");
        }
    }
}
