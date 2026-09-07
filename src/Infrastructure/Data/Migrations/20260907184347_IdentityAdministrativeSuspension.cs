using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanArchitecture.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class IdentityAdministrativeSuspension : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StatusBeforeSuspension",
                table: "AspNetUsers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            // The bootstrap ceremony that grants Platform its system permissions runs once per database, so a new
            // Platform code reaches a fresh install and no existing one. Without this, an operator on a database
            // provisioned before today would hold `platform.tenants.manage` and be unable to stop a single
            // account — and there would be no route to fix it, because editing a Platform role is not something
            // this system does (IA-REQ-042).
            //
            // The catalogue row is written here as well because migrations run before the startup synchronizer
            // that normally owns it, and the grant needs the row to exist. Both statements are idempotent, so the
            // synchronizer finds its work already done rather than conflicting with it.
            migrationBuilder.Sql("""
                INSERT INTO "Permissions" ("Code", "AllowedTenantTypes")
                VALUES ('platform.identities.manage', 'Platform')
                ON CONFLICT ("Code") DO NOTHING;

                INSERT INTO "RolePermissions" ("TenantId", "RoleId", "PermissionCode")
                SELECT held."TenantId", held."RoleId", 'platform.identities.manage'
                FROM "RolePermissions" AS held
                JOIN "Roles" AS r ON r."TenantId" = held."TenantId" AND r."Id" = held."RoleId"
                JOIN "Tenants" AS t ON t."Id" = held."TenantId"
                WHERE held."PermissionCode" = 'platform.tenants.manage'
                  AND t."Type" = 'Platform'
                  AND r."IsSystem"
                ON CONFLICT DO NOTHING;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_AspNetUsers_StatusBeforeSuspension",
                table: "AspNetUsers",
                sql: "(\"StatusBeforeSuspension\" IS NULL) = (\"Status\" <> 'AdministrativelySuspended') AND (\"StatusBeforeSuspension\" IS NULL OR \"StatusBeforeSuspension\" IN ('PendingConfirmation', 'Active', 'SelfDeactivated'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AspNetUsers_StatusBeforeSuspension",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "StatusBeforeSuspension",
                table: "AspNetUsers");
        }
    }
}
