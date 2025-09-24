using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BinStash.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SwitchChunkChecksumToBytea : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_Chunks",
                table: "Chunks");
            
            // Convert hex string (char(64)) -> bytea using PostgreSQL decode()
            // trim() removes CHAR(64) right-padding spaces safely
            migrationBuilder.Sql(@"
ALTER TABLE ""Chunks""
  ALTER COLUMN ""Checksum"" TYPE bytea
  USING decode(trim(trailing from ""Checksum""), 'hex');
");
            // Deduplicate rows that now collide on (ChunkStoreId, Checksum)
            // Keep the row with the largest Length (tie-breaker: lowest ctid)
            migrationBuilder.Sql(@"
WITH ranked AS (
  SELECT
    ctid,
    ROW_NUMBER() OVER (
      PARTITION BY ""ChunkStoreId"", ""Checksum""
      ORDER BY ""Length"" DESC, ctid
    ) AS rn
  FROM ""Chunks""
)
DELETE FROM ""Chunks"" t
USING ranked r
WHERE t.ctid = r.ctid
  AND r.rn > 1;
");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Chunks",
                table: "Chunks",
                columns: new[] { "ChunkStoreId", "Checksum" });

            migrationBuilder.AddCheckConstraint(
                name: "chk_chunks_checksum_len",
                table: "Chunks",
                sql: "octet_length(\"Checksum\") = 32");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_Chunks",
                table: "Chunks");

            migrationBuilder.DropCheckConstraint(
                name: "chk_chunks_checksum_len",
                table: "Chunks");

            // Convert bytea -> fixed-width hex (char(64)) using encode()
            migrationBuilder.Sql(@"
ALTER TABLE ""Chunks""
  ALTER COLUMN ""Checksum"" TYPE char(64)
  USING encode(""Checksum"", 'hex');
");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Chunks",
                table: "Chunks",
                columns: new[] { "Checksum", "ChunkStoreId" });
        }
    }
}
