using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BinStash.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChunkStoreStatsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChunkStoreStatsSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChunkStoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ChunkCount = table.Column<long>(type: "bigint", nullable: false),
                    FileDefinitionCount = table.Column<long>(type: "bigint", nullable: false),
                    ReleaseCount = table.Column<long>(type: "bigint", nullable: false),
                    ChunkPackBytes = table.Column<long>(type: "bigint", nullable: false),
                    FileDefinitionPackBytes = table.Column<long>(type: "bigint", nullable: false),
                    ReleasePackageBytes = table.Column<long>(type: "bigint", nullable: false),
                    IndexBytes = table.Column<long>(type: "bigint", nullable: false),
                    PhysicalBytesTotal = table.Column<long>(type: "bigint", nullable: false),
                    TotalLogicalBytes = table.Column<long>(type: "bigint", nullable: false),
                    UniqueFileBytes = table.Column<long>(type: "bigint", nullable: false),
                    UniqueLogicalChunkBytes = table.Column<long>(type: "bigint", nullable: false),
                    UniqueCompressedChunkBytes = table.Column<long>(type: "bigint", nullable: false),
                    ReferencedUniqueChunkBytes = table.Column<long>(type: "bigint", nullable: false),
                    CompressionRatio = table.Column<double>(type: "double precision", precision: 18, scale: 6, nullable: false),
                    DeduplicationRatio = table.Column<double>(type: "double precision", precision: 18, scale: 6, nullable: false),
                    EffectiveStorageRatio = table.Column<double>(type: "double precision", precision: 18, scale: 6, nullable: false),
                    CompressionSavedBytes = table.Column<long>(type: "bigint", nullable: false),
                    DeduplicationSavedBytes = table.Column<long>(type: "bigint", nullable: false),
                    ChunkPackFileCount = table.Column<int>(type: "integer", nullable: false),
                    FileDefinitionPackFileCount = table.Column<int>(type: "integer", nullable: false),
                    ReleasePackageFileCount = table.Column<int>(type: "integer", nullable: false),
                    IndexFileCount = table.Column<int>(type: "integer", nullable: false),
                    VolumeTotalBytes = table.Column<long>(type: "bigint", nullable: false),
                    VolumeFreeBytes = table.Column<long>(type: "bigint", nullable: false),
                    AvgChunkSize = table.Column<long>(type: "bigint", nullable: false),
                    AvgCompressedChunkSize = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChunkStoreStatsSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChunkStoreStatsSnapshots_ChunkStoreId_CollectedAt",
                table: "ChunkStoreStatsSnapshots",
                columns: new[] { "ChunkStoreId", "CollectedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChunkStoreStatsSnapshots");
        }
    }
}
