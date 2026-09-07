using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanArchitecture.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RetentionLegalHolds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The bootstrap that grants Platform its system permissions runs once per database, so these two
            // codes reach a fresh install and no existing one. Both statements are idempotent, and the catalogue
            // rows are written here because migrations run before the startup synchronizer that owns them
            // (IA-REQ-056).
            migrationBuilder.Sql("""
                INSERT INTO "Permissions" ("Code", "AllowedTenantTypes")
                VALUES ('platform.retention.read', 'Platform'), ('platform.retention.manage', 'Platform')
                ON CONFLICT ("Code") DO NOTHING;

                INSERT INTO "RolePermissions" ("TenantId", "RoleId", "PermissionCode")
                SELECT held."TenantId", held."RoleId", granted."Code"
                FROM "RolePermissions" AS held
                JOIN "Roles" AS r ON r."TenantId" = held."TenantId" AND r."Id" = held."RoleId"
                JOIN "Tenants" AS t ON t."Id" = held."TenantId"
                CROSS JOIN (VALUES ('platform.retention.read'), ('platform.retention.manage')) AS granted("Code")
                WHERE held."PermissionCode" = 'platform.tenants.manage'
                  AND t."Type" = 'Platform'
                  AND r."IsSystem"
                ON CONFLICT DO NOTHING;
                """);

            migrationBuilder.CreateTable(
                name: "RetentionLegalHolds",
                columns: table => new
                {
                    HoldId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectIdentityId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Reference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PlacedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PlacedByMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetentionLegalHolds", x => x.HoldId);
                    table.CheckConstraint("CK_RetentionLegalHolds_Ids_NotEmpty", "\"HoldId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"SubjectIdentityId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"PlacedByMembershipId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_RetentionLegalHolds_Shape", "\"Version\" > 0 AND (\"ReleasedAt\" IS NULL OR \"ReleasedAt\" >= \"PlacedAt\") AND \"ReasonCode\" ~ '^[A-Za-z0-9._:-]{1,64}$' AND \"Reference\" ~ '^[A-Za-z0-9._:-]{1,64}$'");
                    table.ForeignKey(
                        name: "FK_RetentionLegalHolds_AspNetUsers_SubjectIdentityId",
                        column: x => x.SubjectIdentityId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RetentionLegalHolds_SubjectIdentityId",
                table: "RetentionLegalHolds",
                column: "SubjectIdentityId",
                filter: "\"ReleasedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RetentionLegalHolds_SubjectIdentityId_ReasonCode",
                table: "RetentionLegalHolds",
                columns: new[] { "SubjectIdentityId", "ReasonCode" },
                unique: true,
                filter: "\"ReleasedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RetentionLegalHolds");
        }
    }
}
