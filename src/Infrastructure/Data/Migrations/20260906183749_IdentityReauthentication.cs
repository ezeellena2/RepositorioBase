using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanArchitecture.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class IdentityReauthentication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeviceLabel",
                table: "UserSessions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicRef",
                table: "UserSessions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            // Sessions that predate this change get a reference of their own rather than the empty default the
            // column was added with: the unique index below would otherwise collide on the second existing row,
            // and a reference derived from the row's identifier would be the ticket identifier in disguise.
            // uuid_send(gen_random_uuid()) is sixteen random bytes without needing an extension.
            migrationBuilder.Sql("""
                UPDATE "UserSessions"
                SET "DeviceLabel" = 'Other',
                    "PublicRef" = translate(rtrim(encode(uuid_send(gen_random_uuid()), 'base64'), '='), '+/', '-_')
                WHERE "PublicRef" = '';
                """);

            migrationBuilder.CreateTable(
                name: "IdentitySecurityStates",
                columns: table => new
                {
                    IdentityId = table.Column<Guid>(type: "uuid", nullable: false),
                    SecurityVersion = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentitySecurityStates", x => x.IdentityId);
                    table.CheckConstraint("CK_IdentitySecurityStates_State", "\"IdentityId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"SecurityVersion\" >= 0 AND \"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_IdentitySecurityStates_AspNetUsers_IdentityId",
                        column: x => x.IdentityId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RecentIdentityProofs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IdentityId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Method = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SecurityVersion = table.Column<long>(type: "bigint", nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConsumedReason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecentIdentityProofs", x => x.Id);
                    table.CheckConstraint("CK_RecentIdentityProofs_Ids_NotEmpty", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"IdentityId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"SessionId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_RecentIdentityProofs_Lifecycle", "\"SecurityVersion\" >= 0 AND \"Version\" > 0 AND \"ExpiresAt\" > \"IssuedAt\" AND (\"ConsumedAt\" IS NULL) = (\"ConsumedReason\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_RecentIdentityProofs_AspNetUsers_IdentityId",
                        column: x => x.IdentityId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RecentIdentityProofs_UserSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "UserSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserSessions_PublicRef",
                table: "UserSessions",
                column: "PublicRef",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecentIdentityProofs_IdentityId_SessionId_Action",
                table: "RecentIdentityProofs",
                columns: new[] { "IdentityId", "SessionId", "Action" },
                unique: true,
                filter: "\"ConsumedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RecentIdentityProofs_SessionId",
                table: "RecentIdentityProofs",
                column: "SessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IdentitySecurityStates");

            migrationBuilder.DropTable(
                name: "RecentIdentityProofs");

            migrationBuilder.DropIndex(
                name: "IX_UserSessions_PublicRef",
                table: "UserSessions");

            migrationBuilder.DropColumn(
                name: "DeviceLabel",
                table: "UserSessions");

            migrationBuilder.DropColumn(
                name: "PublicRef",
                table: "UserSessions");
        }
    }
}
