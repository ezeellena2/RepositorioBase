using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanArchitecture.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PlatformMfa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlatformMfaEnrollments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IdentityId = table.Column<Guid>(type: "uuid", nullable: false),
                    EncryptedSecret = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RecoveryAcknowledgedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastVerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastVerifiedSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformMfaEnrollments", x => x.Id);
                    table.CheckConstraint("CK_PlatformMfaEnrollments_Ids_NotEmpty", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"IdentityId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND (\"LastVerifiedSessionId\" IS NULL OR \"LastVerifiedSessionId\" <> '00000000-0000-0000-0000-000000000000'::uuid)");
                    table.CheckConstraint("CK_PlatformMfaEnrollments_Lifecycle", "length(\"EncryptedSecret\") > 0 AND ((\"LastVerifiedAt\" IS NULL) = (\"LastVerifiedSessionId\" IS NULL)) AND (\"LastVerifiedAt\" IS NULL OR \"LastVerifiedAt\" >= \"CreatedAt\") AND ((\"Status\" = 'Pending' AND \"VerifiedAt\" IS NULL AND \"RecoveryAcknowledgedAt\" IS NULL AND \"LastVerifiedAt\" IS NULL) OR (\"Status\" = 'Verified' AND \"VerifiedAt\" IS NOT NULL AND \"VerifiedAt\" >= \"CreatedAt\" AND \"RecoveryAcknowledgedAt\" IS NULL) OR (\"Status\" = 'Active' AND \"VerifiedAt\" IS NOT NULL AND \"RecoveryAcknowledgedAt\" IS NOT NULL AND \"RecoveryAcknowledgedAt\" >= \"VerifiedAt\"))");
                    table.ForeignKey(
                        name: "FK_PlatformMfaEnrollments_AspNetUsers_IdentityId",
                        column: x => x.IdentityId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlatformRecoveryCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformRecoveryCodes", x => x.Id);
                    table.CheckConstraint("CK_PlatformRecoveryCodes_Ids_NotEmpty", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"EnrollmentId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_PlatformRecoveryCodes_Lifecycle", "length(\"CodeHash\") > 0 AND (\"ConsumedAt\" IS NULL OR \"ConsumedAt\" >= \"CreatedAt\")");
                    table.ForeignKey(
                        name: "FK_PlatformRecoveryCodes_PlatformMfaEnrollments_EnrollmentId",
                        column: x => x.EnrollmentId,
                        principalTable: "PlatformMfaEnrollments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformMfaEnrollments_IdentityId",
                table: "PlatformMfaEnrollments",
                column: "IdentityId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformRecoveryCodes_EnrollmentId_CodeHash",
                table: "PlatformRecoveryCodes",
                columns: new[] { "EnrollmentId", "CodeHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformRecoveryCodes");

            migrationBuilder.DropTable(
                name: "PlatformMfaEnrollments");
        }
    }
}
