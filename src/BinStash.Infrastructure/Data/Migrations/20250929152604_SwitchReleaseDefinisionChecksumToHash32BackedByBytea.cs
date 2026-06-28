using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BinStash.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SwitchReleaseDefinisionChecksumToHash32BackedByBytea : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Convert hex string (char(64)) -> bytea using PostgreSQL decode()
            // trim() removes CHAR(64) right-padding spaces safely
            migrationBuilder.Sql(@"
ALTER TABLE ""Releases""
  ALTER COLUMN ""ReleaseDefinitionChecksum"" TYPE bytea
  USING decode(trim(trailing from ""ReleaseDefinitionChecksum""), 'hex');
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Convert bytea -> fixed-width hex (char(64)) using encode()
            migrationBuilder.Sql(@"
ALTER TABLE ""Releases""
  ALTER COLUMN ""ReleaseDefinitionChecksum"" TYPE char(64)
  USING encode(""ReleaseDefinitionChecksum"", 'hex');
");
        }
    }
}
