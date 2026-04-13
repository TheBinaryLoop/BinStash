using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BinStash.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class BetterPerReleaseStatsTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TotalUncompressedSize",
                table: "ReleaseMetrics");

            migrationBuilder.DropColumn(
                name: "DataSizeTotal",
                table: "IngestSessions");

            migrationBuilder.DropColumn(
                name: "DataSizeUnique",
                table: "IngestSessions");

            migrationBuilder.AlterColumn<long>(
                name: "NewCompressedBytes",
                table: "ReleaseMetrics",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldDefaultValue: 0L);

            migrationBuilder.AlterColumn<int>(
                name: "NewChunks",
                table: "ReleaseMetrics",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 0);

            migrationBuilder.AlterColumn<int>(
                name: "MetaBytesFullDiff",
                table: "ReleaseMetrics",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 0);

            migrationBuilder.AlterColumn<int>(
                name: "MetaBytesFull",
                table: "ReleaseMetrics",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 0);

            migrationBuilder.AlterColumn<int>(
                name: "FilesInRelease",
                table: "ReleaseMetrics",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 0);

            migrationBuilder.AlterColumn<int>(
                name: "ComponentsInRelease",
                table: "ReleaseMetrics",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 0);

            migrationBuilder.AlterColumn<int>(
                name: "ChunksInRelease",
                table: "ReleaseMetrics",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "CompressionSavedBytes",
                table: "ReleaseMetrics",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "DeduplicationSavedBytes",
                table: "ReleaseMetrics",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<double>(
                name: "IncrementalCompressionRatio",
                table: "ReleaseMetrics",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "IncrementalDeduplicationRatio",
                table: "ReleaseMetrics",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "IncrementalEffectiveRatio",
                table: "ReleaseMetrics",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "NewDataPercent",
                table: "ReleaseMetrics",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<long>(
                name: "NewUniqueLogicalBytes",
                table: "ReleaseMetrics",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalLogicalBytes",
                table: "ReleaseMetrics",
                type: "numeric(20,0)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AlterColumn<int>(
                name: "MetadataSize",
                table: "IngestSessions",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 0);

            migrationBuilder.AlterColumn<long>(
                name: "FilesSeenUnique",
                table: "IngestSessions",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 0);

            migrationBuilder.AlterColumn<long>(
                name: "FilesSeenTotal",
                table: "IngestSessions",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 0);

            migrationBuilder.AlterColumn<long>(
                name: "FilesSeenNew",
                table: "IngestSessions",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 0);

            migrationBuilder.AlterColumn<long>(
                name: "ChunksSeenUnique",
                table: "IngestSessions",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 0);

            migrationBuilder.AlterColumn<long>(
                name: "ChunksSeenTotal",
                table: "IngestSessions",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 0);

            migrationBuilder.AlterColumn<long>(
                name: "ChunksSeenNew",
                table: "IngestSessions",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "NewCompressedBytes",
                table: "IngestSessions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "NewUniqueLogicalBytes",
                table: "IngestSessions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalLogicalBytes",
                table: "IngestSessions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompressionSavedBytes",
                table: "ReleaseMetrics");

            migrationBuilder.DropColumn(
                name: "DeduplicationSavedBytes",
                table: "ReleaseMetrics");

            migrationBuilder.DropColumn(
                name: "IncrementalCompressionRatio",
                table: "ReleaseMetrics");

            migrationBuilder.DropColumn(
                name: "IncrementalDeduplicationRatio",
                table: "ReleaseMetrics");

            migrationBuilder.DropColumn(
                name: "IncrementalEffectiveRatio",
                table: "ReleaseMetrics");

            migrationBuilder.DropColumn(
                name: "NewDataPercent",
                table: "ReleaseMetrics");

            migrationBuilder.DropColumn(
                name: "NewUniqueLogicalBytes",
                table: "ReleaseMetrics");

            migrationBuilder.DropColumn(
                name: "TotalLogicalBytes",
                table: "ReleaseMetrics");

            migrationBuilder.DropColumn(
                name: "NewCompressedBytes",
                table: "IngestSessions");

            migrationBuilder.DropColumn(
                name: "NewUniqueLogicalBytes",
                table: "IngestSessions");

            migrationBuilder.DropColumn(
                name: "TotalLogicalBytes",
                table: "IngestSessions");

            migrationBuilder.AlterColumn<long>(
                name: "NewCompressedBytes",
                table: "ReleaseMetrics",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<int>(
                name: "NewChunks",
                table: "ReleaseMetrics",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "MetaBytesFullDiff",
                table: "ReleaseMetrics",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "MetaBytesFull",
                table: "ReleaseMetrics",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "FilesInRelease",
                table: "ReleaseMetrics",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "ComponentsInRelease",
                table: "ReleaseMetrics",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "ChunksInRelease",
                table: "ReleaseMetrics",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<decimal>(
                name: "TotalUncompressedSize",
                table: "ReleaseMetrics",
                type: "numeric(20,0)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AlterColumn<int>(
                name: "MetadataSize",
                table: "IngestSessions",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "FilesSeenUnique",
                table: "IngestSessions",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<int>(
                name: "FilesSeenTotal",
                table: "IngestSessions",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<int>(
                name: "FilesSeenNew",
                table: "IngestSessions",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<int>(
                name: "ChunksSeenUnique",
                table: "IngestSessions",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<int>(
                name: "ChunksSeenTotal",
                table: "IngestSessions",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<int>(
                name: "ChunksSeenNew",
                table: "IngestSessions",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<long>(
                name: "DataSizeTotal",
                table: "IngestSessions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "DataSizeUnique",
                table: "IngestSessions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }
    }
}
