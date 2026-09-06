using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanArchitecture.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PersonalIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IdentityDocuments",
                columns: table => new
                {
                    IdentityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Country = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DocumentType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Ciphertext = table.Column<string>(type: "text", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PurgedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Classification = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentityDocuments", x => x.IdentityId);
                    table.CheckConstraint("CK_IdentityDocuments_Document", "\"Country\" = 'AR' AND \"DocumentType\" = 'DNI' AND ((\"PurgedAt\" IS NULL) = (\"Ciphertext\" <> '')) AND (\"PurgedAt\" IS NULL OR \"PurgedAt\" >= \"RecordedAt\")");
                    table.CheckConstraint("CK_IdentityDocuments_Id_NotEmpty", "\"IdentityId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_IdentityDocuments_AspNetUsers_IdentityId",
                        column: x => x.IdentityId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PersonalTenantOwnerships",
                columns: table => new
                {
                    IdentityId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonalTenantOwnerships", x => x.IdentityId);
                    table.CheckConstraint("CK_PersonalTenantOwnerships_Ids_NotEmpty", "\"IdentityId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"TenantId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_PersonalTenantOwnerships_AspNetUsers_IdentityId",
                        column: x => x.IdentityId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PersonalTenantOwnerships_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PersonProfiles",
                columns: table => new
                {
                    IdentityId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonalTenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Classification = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonProfiles", x => x.IdentityId);
                    table.CheckConstraint("CK_PersonProfiles_Ids_NotEmpty", "\"IdentityId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"PersonalTenantId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_PersonProfiles_AspNetUsers_IdentityId",
                        column: x => x.IdentityId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PersonProfiles_Tenants_PersonalTenantId",
                        column: x => x.PersonalTenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IdentityDocumentFingerprints",
                columns: table => new
                {
                    IdentityId = table.Column<Guid>(type: "uuid", nullable: false),
                    KeyVersion = table.Column<int>(type: "integer", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentityDocumentFingerprints", x => new { x.IdentityId, x.KeyVersion });
                    table.CheckConstraint("CK_IdentityDocumentFingerprints_Fingerprint", "\"KeyVersion\" >= 1 AND \"Fingerprint\" ~ '^k[1-9][0-9]*:v1:[A-Za-z0-9+/]{43}=$'");
                    table.ForeignKey(
                        name: "FK_IdentityDocumentFingerprints_IdentityDocuments_IdentityId",
                        column: x => x.IdentityId,
                        principalTable: "IdentityDocuments",
                        principalColumn: "IdentityId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UX_IdentityDocumentFingerprints_Fingerprint",
                table: "IdentityDocumentFingerprints",
                column: "Fingerprint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PersonalTenantOwnerships_TenantId",
                table: "PersonalTenantOwnerships",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PersonProfiles_PersonalTenantId",
                table: "PersonProfiles",
                column: "PersonalTenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IdentityDocumentFingerprints");

            migrationBuilder.DropTable(
                name: "PersonalTenantOwnerships");

            migrationBuilder.DropTable(
                name: "PersonProfiles");

            migrationBuilder.DropTable(
                name: "IdentityDocuments");
        }
    }
}
