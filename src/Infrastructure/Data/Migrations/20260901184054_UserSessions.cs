using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanArchitecture.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class UserSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IdentityId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IdleExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AbsoluteExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ActiveTenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSessions", x => x.Id);
                    table.CheckConstraint("CK_UserSessions_Lifecycle", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"IdentityId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"Version\" > 0 AND \"CreatedAt\" <= \"LastSeenAt\" AND \"LastSeenAt\" <= \"IdleExpiresAt\" AND \"IdleExpiresAt\" <= \"AbsoluteExpiresAt\" AND (\"RevokedAt\" IS NULL OR \"RevokedAt\" >= \"CreatedAt\")");
                    table.ForeignKey(
                        name: "FK_UserSessions_AspNetUsers_IdentityId",
                        column: x => x.IdentityId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserSessions_Tenants_ActiveTenantId",
                        column: x => x.ActiveTenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserSessions_ActiveTenantId",
                table: "UserSessions",
                column: "ActiveTenantId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSessions_IdentityId",
                table: "UserSessions",
                column: "IdentityId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSessions_IdentityId_RevokedAt_AbsoluteExpiresAt",
                table: "UserSessions",
                columns: new[] { "IdentityId", "RevokedAt", "AbsoluteExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserSessions");
        }
    }
}
