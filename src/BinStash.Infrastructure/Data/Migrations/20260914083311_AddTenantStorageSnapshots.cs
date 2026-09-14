using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BinStash.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantStorageSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantStorageSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComputedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Duration = table.Column<TimeSpan>(type: "interval", nullable: false),
                    UniqueLogicalBytes = table.Column<long>(type: "bigint", nullable: false),
                    LogicalBytes = table.Column<long>(type: "bigint", nullable: false),
                    UniqueChunkCount = table.Column<long>(type: "bigint", nullable: false),
                    ReleaseCount = table.Column<int>(type: "integer", nullable: false),
                    RepositoryCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantStorageSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantStorageSnapshots_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenantStorageSnapshots_TenantId_ComputedAt",
                table: "TenantStorageSnapshots",
                columns: new[] { "TenantId", "ComputedAt" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantStorageSnapshots");
        }
    }
}
