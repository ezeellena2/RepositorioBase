using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanArchitecture.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PlatformAdminInvitation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlatformAdminInvitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Delivery = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsOwner = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    BoundIdentityId = table.Column<Guid>(type: "uuid", nullable: true),
                    BoundAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeliveryMessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeliverySettledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformAdminInvitations", x => x.Id);
                    table.CheckConstraint("CK_PlatformAdminInvitations_Ids_NotEmpty", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"TenantId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND (\"BoundIdentityId\" IS NULL OR \"BoundIdentityId\" <> '00000000-0000-0000-0000-000000000000'::uuid)");
                    table.CheckConstraint("CK_PlatformAdminInvitations_Lifecycle", "\"TokenHash\" ~ '^v1:[A-Za-z0-9+/]{42}[AEIMQUYcgkosw048]=$' AND \"NormalizedEmail\" = normalize(\"NormalizedEmail\", NFC) AND \"NormalizedEmail\" = lower(\"NormalizedEmail\") AND \"NormalizedEmail\" !~ '[[:space:]]' AND position(U&'\\00a0' IN \"NormalizedEmail\") = 0 AND strpos(\"NormalizedEmail\", '@') > 0 AND \"ExpiresAt\" > \"CreatedAt\" AND ((\"BoundIdentityId\" IS NULL) = (\"BoundAt\" IS NULL)) AND (\"BoundAt\" IS NULL OR \"BoundAt\" >= \"CreatedAt\") AND ((\"Delivery\" = 'Pending') = (\"DeliverySettledAt\" IS NULL)) AND (\"DeliverySettledAt\" IS NULL OR \"DeliverySettledAt\" >= \"CreatedAt\") AND ((\"Status\" = 'Pending' AND \"AcceptedAt\" IS NULL AND \"CancelledAt\" IS NULL) OR (\"Status\" = 'Accepted' AND \"BoundIdentityId\" IS NOT NULL AND \"AcceptedAt\" IS NOT NULL AND \"AcceptedAt\" >= \"CreatedAt\" AND \"AcceptedAt\" <= \"ExpiresAt\" AND \"CancelledAt\" IS NULL) OR (\"Status\" = 'Cancelled' AND \"AcceptedAt\" IS NULL AND \"CancelledAt\" IS NOT NULL AND \"CancelledAt\" >= \"CreatedAt\"))");
                    table.ForeignKey(
                        name: "FK_PlatformAdminInvitations_AspNetUsers_BoundIdentityId",
                        column: x => x.BoundIdentityId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PlatformAdminInvitations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformAdminInvitations_BoundIdentityId",
                table: "PlatformAdminInvitations",
                column: "BoundIdentityId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformAdminInvitations_ExpiresAt",
                table: "PlatformAdminInvitations",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformAdminInvitations_IsOwner",
                table: "PlatformAdminInvitations",
                column: "IsOwner",
                unique: true,
                filter: "\"Status\" = 'Pending' AND \"IsOwner\"");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformAdminInvitations_NormalizedEmail",
                table: "PlatformAdminInvitations",
                column: "NormalizedEmail",
                unique: true,
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformAdminInvitations_TenantId",
                table: "PlatformAdminInvitations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformAdminInvitations_TokenHash",
                table: "PlatformAdminInvitations",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformAdminInvitations");
        }
    }
}
