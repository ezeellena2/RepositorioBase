using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanArchitecture.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class DocumentDisputes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The bootstrap that grants Platform its system permissions runs once per database, so this code
            // reaches a fresh install and no existing one. Idempotent, and the catalogue row is written here
            // because migrations run before the startup synchronizer that owns it (IA-REQ-058).
            migrationBuilder.Sql("""
                INSERT INTO "Permissions" ("Code", "AllowedTenantTypes")
                VALUES ('platform.identities.documents.resolve', 'Platform')
                ON CONFLICT ("Code") DO NOTHING;

                INSERT INTO "RolePermissions" ("TenantId", "RoleId", "PermissionCode")
                SELECT held."TenantId", held."RoleId", 'platform.identities.documents.resolve'
                FROM "RolePermissions" AS held
                JOIN "Roles" AS r ON r."TenantId" = held."TenantId" AND r."Id" = held."RoleId"
                JOIN "Tenants" AS t ON t."Id" = held."TenantId"
                WHERE held."PermissionCode" = 'platform.tenants.manage'
                  AND t."Type" = 'Platform'
                  AND r."IsSystem"
                ON CONFLICT DO NOTHING;
                """);

            migrationBuilder.CreateTable(
                name: "IdentityDocumentCorrectionRecords",
                columns: table => new
                {
                    RecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectIdentityId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisputeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedByMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceReference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PreviousKeyVersions = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentityDocumentCorrectionRecords", x => x.RecordId);
                    table.CheckConstraint("CK_IdentityDocumentCorrectionRecords_Evidence", "\"EvidenceReference\" ~ '^[A-Za-z0-9._:-]{1,64}$' AND \"PreviousKeyVersions\" ~ '^[0-9,]*$'");
                    table.CheckConstraint("CK_IdentityDocumentCorrectionRecords_Ids_NotEmpty", "\"RecordId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"SubjectIdentityId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"DisputeId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"ResolvedByMembershipId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_IdentityDocumentCorrectionRecords_AspNetUsers_SubjectIdenti~",
                        column: x => x.SubjectIdentityId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IdentityDocumentDisputes",
                columns: table => new
                {
                    DisputeId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectIdentityId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClaimedCiphertext = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OpenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentityDocumentDisputes", x => x.DisputeId);
                    table.CheckConstraint("CK_IdentityDocumentDisputes_Ids_NotEmpty", "\"DisputeId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"SubjectIdentityId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_IdentityDocumentDisputes_Lifecycle", "\"Version\" > 0 AND (\"Status\" = 'Open') = (\"ResolvedAt\" IS NULL) AND (\"ResolvedAt\" IS NULL OR \"ResolvedAt\" >= \"OpenedAt\") AND \"ReasonCode\" ~ '^[A-Za-z0-9._:-]{1,64}$' AND length(\"ClaimedCiphertext\") > 0");
                    table.ForeignKey(
                        name: "FK_IdentityDocumentDisputes_AspNetUsers_SubjectIdentityId",
                        column: x => x.SubjectIdentityId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IdentityDocumentCorrectionRecords_DisputeId",
                table: "IdentityDocumentCorrectionRecords",
                column: "DisputeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdentityDocumentCorrectionRecords_SubjectIdentityId",
                table: "IdentityDocumentCorrectionRecords",
                column: "SubjectIdentityId");

            migrationBuilder.CreateIndex(
                name: "IX_IdentityDocumentDisputes_SubjectIdentityId",
                table: "IdentityDocumentDisputes",
                column: "SubjectIdentityId",
                unique: true,
                filter: "\"Status\" = 'Open'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IdentityDocumentCorrectionRecords");

            migrationBuilder.DropTable(
                name: "IdentityDocumentDisputes");
        }
    }
}
