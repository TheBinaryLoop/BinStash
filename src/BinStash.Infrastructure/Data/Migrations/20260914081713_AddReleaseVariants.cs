using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BinStash.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Moves the release definition from the release to a per-target variant, and gives every
    /// existing release one "default" variant so nothing has to know targets exist.
    ///
    /// <para>
    /// The scaffolded version of this migration dropped Releases.ReleaseDefinitionChecksum before
    /// anything had been backfilled from it. That column is the only pointer from a release to its
    /// .rdef, and the garbage collector treats whatever it cannot reach from those pointers as
    /// collectable — so losing it does not merely break downloads, it makes the next collection
    /// quarantine the entire chunk store. Every statement below is ordered so that the old columns
    /// are read before they are dropped, and guarded so that a backfill which did not cover
    /// everything aborts the migration instead of completing it.
    /// </para>
    ///
    /// <para>
    /// PostgreSQL runs DDL transactionally and EF wraps a migration in one transaction, so any
    /// RAISE EXCEPTION below rolls the whole thing back and leaves the database untouched.
    /// </para>
    /// </summary>
    public partial class AddReleaseVariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- Pre-flight -------------------------------------------------
            // (RepoId, Version) is about to become unique. It never was, and it was only ever
            // enforced by an Any() check that a concurrent publish can lose, so an existing
            // database may genuinely hold duplicates. Naming them beats a bare index violation:
            // resolving them is a judgement call about which release is the real one.
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    duplicates text;
                BEGIN
                    SELECT string_agg(format('%s (repo %s, %s rows)', "Version", "RepoId", cnt), '; ')
                      INTO duplicates
                      FROM (
                          SELECT "RepoId", "Version", count(*) AS cnt
                            FROM "Releases"
                        GROUP BY "RepoId", "Version"
                          HAVING count(*) > 1
                      ) d;

                    IF duplicates IS NOT NULL THEN
                        RAISE EXCEPTION
                            'Cannot make (RepoId, Version) unique: duplicate releases exist — %. Resolve them before migrating.',
                            duplicates;
                    END IF;
                END $$;
                """);

            // ---- 1. The variant table --------------------------------------
            migrationBuilder.CreateTable(
                name: "ReleaseVariants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TargetAttributes = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    ReleaseDefinitionChecksum = table.Column<byte[]>(type: "bytea", nullable: false),
                    SerializerVersion = table.Column<byte>(type: "smallint", nullable: false, defaultValue: (byte)0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseVariants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReleaseVariants_Releases_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "Releases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // ---- 2. Backfill one variant per existing release ---------------
            // CreatedAt is carried across rather than defaulted: a backfilled variant that claimed
            // to have been created at migration time would silently rewrite the publication date
            // of every release in the instance.
            migrationBuilder.Sql("""
                INSERT INTO "ReleaseVariants"
                    ("Id", "ReleaseId", "TargetKey", "TargetAttributes", "CreatedAt", "ReleaseDefinitionChecksum", "SerializerVersion")
                SELECT
                    gen_random_uuid(),
                    r."Id",
                    'default',
                    NULL,
                    r."CreatedAt",
                    r."ReleaseDefinitionChecksum",
                    r."SerializerVersion"
                FROM "Releases" r;
                """);

            // ---- 3. Prove the backfill is complete before dropping anything --
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    releases bigint;
                    variants bigint;
                    unmatched bigint;
                BEGIN
                    SELECT count(*) INTO releases FROM "Releases";
                    SELECT count(*) INTO variants FROM "ReleaseVariants";

                    IF releases <> variants THEN
                        RAISE EXCEPTION
                            'Backfill incomplete: % releases produced % variants. Refusing to drop the release definition columns.',
                            releases, variants;
                    END IF;

                    -- A release whose variant carries no checksum would be unreadable and, worse,
                    -- unreachable: the collector would treat everything it points at as garbage.
                    SELECT count(*) INTO unmatched
                      FROM "ReleaseVariants" v
                     WHERE v."ReleaseDefinitionChecksum" IS NULL
                        OR octet_length(v."ReleaseDefinitionChecksum") = 0;

                    IF unmatched > 0 THEN
                        RAISE EXCEPTION
                            'Backfill produced % variant(s) with an empty release definition checksum. Refusing to continue.',
                            unmatched;
                    END IF;
                END $$;
                """);

            // ---- 4. Re-key ReleaseMetrics from the release to its variant ----
            // The rename preserves the column's VALUES, which are release ids. They have to be
            // remapped to variant ids before the foreign key goes on, or every existing metrics
            // row points at a variant that does not exist.
            migrationBuilder.RenameColumn(
                name: "ReleaseId",
                table: "ReleaseMetrics",
                newName: "VariantId");

            migrationBuilder.Sql("""
                UPDATE "ReleaseMetrics" m
                   SET "VariantId" = v."Id"
                  FROM "ReleaseVariants" v
                 WHERE v."ReleaseId" = m."VariantId";
                """);

            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    orphaned bigint;
                BEGIN
                    SELECT count(*) INTO orphaned
                      FROM "ReleaseMetrics" m
                     WHERE NOT EXISTS (SELECT 1 FROM "ReleaseVariants" v WHERE v."Id" = m."VariantId");

                    IF orphaned > 0 THEN
                        RAISE EXCEPTION
                            '% ReleaseMetrics row(s) do not map to a variant. They referenced a release that no longer exists; resolve them before migrating.',
                            orphaned;
                    END IF;
                END $$;
                """);

            // ---- 5. Only now is it safe to drop the old columns --------------
            migrationBuilder.DropColumn(
                name: "ReleaseDefinitionChecksum",
                table: "Releases");

            migrationBuilder.DropColumn(
                name: "SerializerVersion",
                table: "Releases");

            // ---- 6. Constraints and the rest of the schema -------------------
            migrationBuilder.AddColumn<string>(
                name: "TargetKey",
                table: "IngestSessions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            // Superseded by the composite index below, which has RepoId as its leading column.
            migrationBuilder.DropIndex(
                name: "IX_Releases_RepoId",
                table: "Releases");

            migrationBuilder.CreateIndex(
                name: "IX_Releases_RepoId_Version",
                table: "Releases",
                columns: new[] { "RepoId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseVariants_ReleaseId_TargetKey",
                table: "ReleaseVariants",
                columns: new[] { "ReleaseId", "TargetKey" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ReleaseMetrics_ReleaseVariants_VariantId",
                table: "ReleaseMetrics",
                column: "VariantId",
                principalTable: "ReleaseVariants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reversible only while every release still has exactly one variant — a single column
            // cannot hold the definitions of five targets. Refusing is the only honest option: the
            // alternative is picking one target's definition arbitrarily and discarding the rest,
            // which would look like it worked.
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    multi bigint;
                BEGIN
                    SELECT count(*) INTO multi
                      FROM (
                          SELECT "ReleaseId" FROM "ReleaseVariants" GROUP BY "ReleaseId" HAVING count(*) > 1
                      ) d;

                    IF multi > 0 THEN
                        RAISE EXCEPTION
                            'Cannot revert: % release(s) have more than one variant, and a release can carry only one definition. Remove the extra variants first.',
                            multi;
                    END IF;
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_ReleaseMetrics_ReleaseVariants_VariantId",
                table: "ReleaseMetrics");

            migrationBuilder.DropIndex(
                name: "IX_Releases_RepoId_Version",
                table: "Releases");

            migrationBuilder.CreateIndex(
                name: "IX_Releases_RepoId",
                table: "Releases",
                column: "RepoId");

            migrationBuilder.AddColumn<byte[]>(
                name: "ReleaseDefinitionChecksum",
                table: "Releases",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte>(
                name: "SerializerVersion",
                table: "Releases",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);

            // Restore the definitions from the variants before the variants go away.
            migrationBuilder.Sql("""
                UPDATE "Releases" r
                   SET "ReleaseDefinitionChecksum" = v."ReleaseDefinitionChecksum",
                       "SerializerVersion"         = v."SerializerVersion"
                  FROM "ReleaseVariants" v
                 WHERE v."ReleaseId" = r."Id";
                """);

            // Map the metrics back to their release while the variants are still there to join on.
            migrationBuilder.Sql("""
                UPDATE "ReleaseMetrics" m
                   SET "VariantId" = v."ReleaseId"
                  FROM "ReleaseVariants" v
                 WHERE v."Id" = m."VariantId";
                """);

            migrationBuilder.RenameColumn(
                name: "VariantId",
                table: "ReleaseMetrics",
                newName: "ReleaseId");

            migrationBuilder.DropColumn(
                name: "TargetKey",
                table: "IngestSessions");

            migrationBuilder.DropTable(
                name: "ReleaseVariants");
        }
    }
}
