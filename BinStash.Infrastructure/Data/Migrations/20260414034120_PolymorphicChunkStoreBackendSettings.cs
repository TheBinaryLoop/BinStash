using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BinStash.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PolymorphicChunkStoreBackendSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Add the new jsonb column (nullable initially for the data migration)
            migrationBuilder.AddColumn<string>(
                name: "BackendSettings",
                table: "ChunkStores",
                type: "jsonb",
                nullable: true);

            // 2. Migrate existing LocalPath values into the new BackendSettings JSON column
            // NOTE: Property names must use camelCase to match the JsonSerializerOptions
            // used by the EF Core value converter (PropertyNamingPolicy = CamelCase).
            migrationBuilder.Sql("""
                UPDATE "ChunkStores"
                SET "BackendSettings" = jsonb_build_object(
                    '$type', 'LocalFolder',
                    'path', "LocalPath"
                )
                WHERE "LocalPath" IS NOT NULL;
                """);

            // 3. Set a fallback for any rows with NULL LocalPath (shouldn't exist, but be safe)
            migrationBuilder.Sql("""
                UPDATE "ChunkStores"
                SET "BackendSettings" = '{"$type":"LocalFolder","path":""}'::jsonb
                WHERE "BackendSettings" IS NULL;
                """);

            // 4. Make the column non-nullable now that all rows have values
            migrationBuilder.AlterColumn<string>(
                name: "BackendSettings",
                table: "ChunkStores",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            // 5. Drop the old LocalPath column
            migrationBuilder.DropColumn(
                name: "LocalPath",
                table: "ChunkStores");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 1. Re-add the LocalPath column (nullable initially)
            migrationBuilder.AddColumn<string>(
                name: "LocalPath",
                table: "ChunkStores",
                type: "text",
                nullable: true);

            // 2. Migrate BackendSettings.Path back to LocalPath
            migrationBuilder.Sql("""
                UPDATE "ChunkStores"
                SET "LocalPath" = "BackendSettings"->>'Path'
                WHERE "BackendSettings" IS NOT NULL;
                """);

            // 3. Make LocalPath non-nullable
            migrationBuilder.AlterColumn<string>(
                name: "LocalPath",
                table: "ChunkStores",
                type: "text",
                nullable: false,
                defaultValue: "");

            // 4. Drop BackendSettings
            migrationBuilder.DropColumn(
                name: "BackendSettings",
                table: "ChunkStores");
        }
    }
}
