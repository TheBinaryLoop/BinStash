using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BinStash.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStorageClassMappingAndDefaultMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StorageClasses",
                columns: table => new
                {
                    Name = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    IsDeprecated = table.Column<bool>(type: "boolean", nullable: false),
                    MaxChunkBytes = table.Column<int>(type: "integer", nullable: false, defaultValue: 16777216)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorageClasses", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "StorageClassDefaultMappings",
                columns: table => new
                {
                    StorageClassName = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ChunkStoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorageClassDefaultMappings", x => x.StorageClassName);
                    table.ForeignKey(
                        name: "FK_StorageClassDefaultMappings_ChunkStores_ChunkStoreId",
                        column: x => x.ChunkStoreId,
                        principalTable: "ChunkStores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StorageClassDefaultMappings_StorageClasses_StorageClassName",
                        column: x => x.StorageClassName,
                        principalTable: "StorageClasses",
                        principalColumn: "Name",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StorageClassMappings",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageClassName = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ChunkStoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorageClassMappings", x => new { x.TenantId, x.StorageClassName });
                    table.ForeignKey(
                        name: "FK_StorageClassMappings_ChunkStores_ChunkStoreId",
                        column: x => x.ChunkStoreId,
                        principalTable: "ChunkStores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StorageClassMappings_StorageClasses_StorageClassName",
                        column: x => x.StorageClassName,
                        principalTable: "StorageClasses",
                        principalColumn: "Name",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StorageClassMappings_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_TenantId_StorageClass",
                table: "Repositories",
                columns: new[] { "TenantId", "StorageClass" });

            migrationBuilder.CreateIndex(
                name: "IX_StorageClassDefaultMappings_ChunkStoreId",
                table: "StorageClassDefaultMappings",
                column: "ChunkStoreId");

            migrationBuilder.CreateIndex(
                name: "IX_StorageClassMappings_ChunkStoreId",
                table: "StorageClassMappings",
                column: "ChunkStoreId");

            migrationBuilder.CreateIndex(
                name: "IX_StorageClassMappings_StorageClassName",
                table: "StorageClassMappings",
                column: "StorageClassName");

            migrationBuilder.CreateIndex(
                name: "IX_StorageClassMappings_TenantId_IsDefault",
                table: "StorageClassMappings",
                columns: new[] { "TenantId", "IsDefault" },
                unique: true,
                filter: "\"IsDefault\" = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StorageClassDefaultMappings");

            migrationBuilder.DropTable(
                name: "StorageClassMappings");

            migrationBuilder.DropTable(
                name: "StorageClasses");

            migrationBuilder.DropIndex(
                name: "IX_Repositories_TenantId_StorageClass",
                table: "Repositories");
        }
    }
}
