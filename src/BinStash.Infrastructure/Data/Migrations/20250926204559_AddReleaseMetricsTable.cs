using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BinStash.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReleaseMetricsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReleaseMetrics",
                columns: table => new
                {
                    ReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    IngestSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    ChunksInRelease = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    NewChunks = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    TotalUncompressedSize = table.Column<decimal>(type: "numeric(20,0)", nullable: false, defaultValue: 0m),
                    NewCompressedBytes = table.Column<decimal>(type: "numeric(20,0)", nullable: false, defaultValue: 0m),
                    MetaBytesFull = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    MetaBytesFullDiff = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    ComponentsInRelease = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    FilesInRelease = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseMetrics", x => x.ReleaseId);
                    table.ForeignKey(
                        name: "FK_ReleaseMetrics_IngestSessions_IngestSessionId",
                        column: x => x.IngestSessionId,
                        principalTable: "IngestSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseMetrics_CreatedAt",
                table: "ReleaseMetrics",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseMetrics_IngestSessionId",
                table: "ReleaseMetrics",
                column: "IngestSessionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReleaseMetrics");
        }
    }
}
