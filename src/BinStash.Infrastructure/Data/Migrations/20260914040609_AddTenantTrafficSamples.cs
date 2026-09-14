using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BinStash.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantTrafficSamples : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantTrafficSamples",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    BucketStartUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Grain = table.Column<int>(type: "integer", nullable: false),
                    IngressBytes = table.Column<long>(type: "bigint", nullable: false),
                    EgressBytes = table.Column<long>(type: "bigint", nullable: false),
                    RequestCount = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantTrafficSamples", x => new { x.TenantId, x.BucketStartUtc, x.Grain });
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenantTrafficSamples_Grain_BucketStartUtc",
                table: "TenantTrafficSamples",
                columns: new[] { "Grain", "BucketStartUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantTrafficSamples");
        }
    }
}
