using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanArchitecture.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class TenantAuthorization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Permissions",
                columns: table => new
                {
                    Code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AllowedTenantTypes = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permissions", x => x.Code);
                    table.CheckConstraint("CK_Permissions_Code_NotEmpty", "length(\"Code\") > 0");
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    IsRetired = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                    table.UniqueConstraint("AK_Roles_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Roles_Ids_NotEmpty", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"TenantId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_Roles_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql("""
                CREATE FUNCTION "PreventSystemRoleDeletion"()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF OLD."IsSystem" THEN
                        RAISE EXCEPTION 'System roles cannot be deleted.';
                    END IF;

                    RETURN OLD;
                END;
                $$;

                CREATE TRIGGER "TR_Roles_PreventSystemDeletion"
                BEFORE DELETE ON "Roles"
                FOR EACH ROW EXECUTE FUNCTION "PreventSystemRoleDeletion"();
                """);

            migrationBuilder.CreateTable(
                name: "MembershipRoles",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MembershipRoles", x => new { x.TenantId, x.MembershipId, x.RoleId });
                    table.CheckConstraint("CK_MembershipRoles_TenantId_NotEmpty", "\"TenantId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_MembershipRoles_Roles_TenantId_RoleId",
                        columns: x => new { x.TenantId, x.RoleId },
                        principalTable: "Roles",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MembershipRoles_TenantMemberships_TenantId_MembershipId",
                        columns: x => new { x.TenantId, x.MembershipId },
                        principalTable: "TenantMemberships",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RolePermissions",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    PermissionCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissions", x => new { x.TenantId, x.RoleId, x.PermissionCode });
                    table.CheckConstraint("CK_RolePermissions_TenantId_NotEmpty", "\"TenantId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_RolePermissions_Permissions_PermissionCode",
                        column: x => x.PermissionCode,
                        principalTable: "Permissions",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RolePermissions_Roles_TenantId_RoleId",
                        columns: x => new { x.TenantId, x.RoleId },
                        principalTable: "Roles",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MembershipRoles_TenantId_RoleId",
                table: "MembershipRoles",
                columns: new[] { "TenantId", "RoleId" });

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_PermissionCode",
                table: "RolePermissions",
                column: "PermissionCode");

            migrationBuilder.CreateIndex(
                name: "IX_Roles_TenantId_NormalizedName",
                table: "Roles",
                columns: new[] { "TenantId", "NormalizedName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS "TR_Roles_PreventSystemDeletion" ON "Roles";
                DROP FUNCTION IF EXISTS "PreventSystemRoleDeletion"();
                """);

            migrationBuilder.DropTable(
                name: "MembershipRoles");

            migrationBuilder.DropTable(
                name: "RolePermissions");

            migrationBuilder.DropTable(
                name: "Permissions");

            migrationBuilder.DropTable(
                name: "Roles");
        }
    }
}
