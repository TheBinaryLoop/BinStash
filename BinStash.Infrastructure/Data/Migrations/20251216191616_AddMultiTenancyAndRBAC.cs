using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BinStash.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiTenancyAndRBAC : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var defaultTenantId = new Guid("070a5385-cd25-4cbc-9493-a9e298bb5a02");

            // Create the tenants table first
            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });
            
            // Insert the default tenant
            migrationBuilder.InsertData(
                table: "Tenants",
                columns: new[] { "Id", "Slug", "Name" },
                values: new object[] { defaultTenantId, "default", "Default Tenant" }
            );

            // Add the TenantId column as nullable first (no default)
            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Repositories",
                type: "uuid",
                nullable: true);
            
            // Backfill existing repositories
            migrationBuilder.Sql($@"
                UPDATE ""Repositories""
                SET ""TenantId"" = '{defaultTenantId}'
                WHERE ""TenantId"" IS NULL;
            ");
            
            //  Alter column to NOT NULL
            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "Repositories",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true
            );

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "Repositories",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now() at time zone 'utc'");
            
            migrationBuilder.CreateTable(
                name: "RepositoryRoleAssignments",
                columns: table => new
                {
                    RepositoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectType = table.Column<short>(type: "smallint", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    GrantedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepositoryRoleAssignments", x => new { x.RepositoryId, x.SubjectType, x.SubjectId });
                    table.ForeignKey(
                        name: "FK_RepositoryRoleAssignments_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TenantRoleAssignments",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    GrantedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantRoleAssignments", x => new { x.TenantId, x.UserId, x.RoleName });
                });

            migrationBuilder.CreateTable(
                name: "ServiceAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Disabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceAccounts_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TenantMembers",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantMembers", x => new { x.TenantId, x.UserId });
                    table.ForeignKey(
                        name: "FK_TenantMembers_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserGroups_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApiKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    SecretHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Scopes = table.Column<string[]>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApiKeys_ServiceAccounts_ServiceAccountId",
                        column: x => x.ServiceAccountId,
                        principalTable: "ServiceAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserGroupMembers",
                columns: table => new
                {
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserGroupMembers", x => new { x.GroupId, x.UserId });
                    table.ForeignKey(
                        name: "FK_UserGroupMembers_UserGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "UserGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_TenantId",
                table: "Repositories",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_TenantId_Name",
                table: "Repositories",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_ServiceAccountId",
                table: "ApiKeys",
                column: "ServiceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryRoleAssignments_RepositoryId",
                table: "RepositoryRoleAssignments",
                column: "RepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryRoleAssignments_SubjectType_SubjectId_RepositoryId",
                table: "RepositoryRoleAssignments",
                columns: new[] { "SubjectType", "SubjectId", "RepositoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceAccounts_TenantId",
                table: "ServiceAccounts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceAccounts_TenantId_Name",
                table: "ServiceAccounts",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantMembers_UserId",
                table: "TenantMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantRoleAssignments_UserId_TenantId",
                table: "TenantRoleAssignments",
                columns: new[] { "UserId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Slug",
                table: "Tenants",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserGroupMembers_UserId",
                table: "UserGroupMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserGroups_TenantId",
                table: "UserGroups",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_UserGroups_TenantId_Name",
                table: "UserGroups",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Repositories_Tenants_TenantId",
                table: "Repositories",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Repositories_Tenants_TenantId",
                table: "Repositories");

            migrationBuilder.DropTable(
                name: "ApiKeys");

            migrationBuilder.DropTable(
                name: "RepositoryRoleAssignments");

            migrationBuilder.DropTable(
                name: "TenantMembers");

            migrationBuilder.DropTable(
                name: "TenantRoleAssignments");

            migrationBuilder.DropTable(
                name: "UserGroupMembers");

            migrationBuilder.DropTable(
                name: "ServiceAccounts");

            migrationBuilder.DropTable(
                name: "UserGroups");

            migrationBuilder.DropTable(
                name: "Tenants");

            migrationBuilder.DropIndex(
                name: "IX_Repositories_TenantId",
                table: "Repositories");

            migrationBuilder.DropIndex(
                name: "IX_Repositories_TenantId_Name",
                table: "Repositories");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Repositories");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Repositories");
        }
    }
}
