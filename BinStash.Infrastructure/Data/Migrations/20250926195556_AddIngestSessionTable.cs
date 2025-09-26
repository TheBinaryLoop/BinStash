using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BinStash.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIngestSessionTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IngestSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RepoId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    LastUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    ChunksSeenTotal = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    ChunksSeenUnique = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    ChunksSeenNew = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    DataSizeTotal = table.Column<decimal>(type: "numeric(20,0)", nullable: false, defaultValue: 0m),
                    DataSizeUnique = table.Column<decimal>(type: "numeric(20,0)", nullable: false, defaultValue: 0m)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngestSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IngestSessions_Repositories_RepoId",
                        column: x => x.RepoId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IngestSessions_RepoId",
                table: "IngestSessions",
                column: "RepoId");

            migrationBuilder.CreateIndex(
                name: "IX_IngestSessions_State",
                table: "IngestSessions",
                column: "State");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IngestSessions");
        }
    }
}
