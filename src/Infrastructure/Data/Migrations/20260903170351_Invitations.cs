using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanArchitecture.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Invitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Invitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AcceptedByIdentityId = table.Column<Guid>(type: "uuid", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invitations", x => x.Id);
                    table.UniqueConstraint("AK_Invitations_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Invitations_Ids_NotEmpty", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"TenantId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND (\"AcceptedByIdentityId\" IS NULL OR \"AcceptedByIdentityId\" <> '00000000-0000-0000-0000-000000000000'::uuid)");
                    table.CheckConstraint("CK_Invitations_Lifecycle", "\"TokenHash\" ~ '^v[0123456789]+:' AND \"NormalizedEmail\" !~ '[ABCDEFGHIJKLMNOPQRSTUVWXYZ]' AND \"NormalizedEmail\" = btrim(\"NormalizedEmail\") AND strpos(\"NormalizedEmail\", '@') > 0 AND \"ExpiresAt\" > \"CreatedAt\" AND ((\"Status\" = 'Pending' AND \"AcceptedByIdentityId\" IS NULL AND \"AcceptedAt\" IS NULL AND \"CancelledAt\" IS NULL) OR (\"Status\" = 'Accepted' AND \"AcceptedByIdentityId\" IS NOT NULL AND \"AcceptedAt\" IS NOT NULL AND \"AcceptedAt\" >= \"CreatedAt\" AND \"AcceptedAt\" <= \"ExpiresAt\" AND \"CancelledAt\" IS NULL) OR (\"Status\" = 'Cancelled' AND \"AcceptedByIdentityId\" IS NULL AND \"AcceptedAt\" IS NULL AND \"CancelledAt\" IS NOT NULL AND \"CancelledAt\" >= \"CreatedAt\"))");
                    table.ForeignKey(
                        name: "FK_Invitations_AspNetUsers_AcceptedByIdentityId",
                        column: x => x.AcceptedByIdentityId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Invitations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvitationRoles",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvitationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvitationRoles", x => new { x.TenantId, x.InvitationId, x.RoleId });
                    table.CheckConstraint("CK_InvitationRoles_TenantId_NotEmpty", "\"TenantId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_InvitationRoles_Invitations_TenantId_InvitationId",
                        columns: x => new { x.TenantId, x.InvitationId },
                        principalTable: "Invitations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvitationRoles_Roles_TenantId_RoleId",
                        columns: x => new { x.TenantId, x.RoleId },
                        principalTable: "Roles",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            // A row-level CHECK cannot express transition immutability, so a settled invitation is protected the
            // same way the repository already protects audit rows and system roles: with a trigger. Without it a
            // single UPDATE - raw SQL, or ExecuteUpdate against the exposed DbSet - could restore Pending and
            // erase the acceptance evidence, bypassing both the aggregate and its concurrency token.
            //
            // The guard compares the whole row rather than an enumerated column list, so a column added later is
            // frozen automatically instead of being silently mutable until someone remembers to extend it.
            migrationBuilder.Sql("""
                CREATE FUNCTION "PreventSettledInvitationChange"() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    IF OLD."Status" <> 'Pending' AND NEW IS DISTINCT FROM OLD THEN
                        RAISE EXCEPTION 'A settled invitation cannot be changed.';
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER "TR_Invitations_PreventSettledChange"
                BEFORE UPDATE ON "Invitations"
                FOR EACH ROW EXECUTE FUNCTION "PreventSettledInvitationChange"();
                """);

            migrationBuilder.CreateIndex(
                name: "IX_InvitationRoles_TenantId_RoleId",
                table: "InvitationRoles",
                columns: new[] { "TenantId", "RoleId" });

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_AcceptedByIdentityId",
                table: "Invitations",
                column: "AcceptedByIdentityId");

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_ExpiresAt",
                table: "Invitations",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_TenantId_NormalizedEmail",
                table: "Invitations",
                columns: new[] { "TenantId", "NormalizedEmail" },
                unique: true,
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_TenantId_NormalizedEmail_Status",
                table: "Invitations",
                columns: new[] { "TenantId", "NormalizedEmail", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_TokenHash",
                table: "Invitations",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS "TR_Invitations_PreventSettledChange" ON "Invitations";
                DROP FUNCTION IF EXISTS "PreventSettledInvitationChange"();
                """);

            migrationBuilder.DropTable(
                name: "InvitationRoles");

            migrationBuilder.DropTable(
                name: "Invitations");
        }
    }
}
