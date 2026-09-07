using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanArchitecture.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class IdentityAccountLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "AspNetUsers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "PendingConfirmation");

            // Every existing account keeps the answer it already gave. Before this column, "may this identity
            // act" was `EmailConfirmed`, so that is exactly what each row is mapped onto — no account's meaning
            // changes on the day the column arrives (IA-REQ-054). The column's own default is the least any
            // account may do, so a later insert that has never heard of this column produces an account nobody
            // can sign into rather than one whose state means nothing.
            migrationBuilder.Sql(
                "UPDATE \"AspNetUsers\" SET \"Status\" = CASE WHEN \"EmailConfirmed\" THEN 'Active' ELSE 'PendingConfirmation' END");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AspNetUsers_Status",
                table: "AspNetUsers",
                sql: "\"Status\" IN ('PendingConfirmation', 'Active', 'SelfDeactivated', 'AdministrativelySuspended', 'Closed')");

            migrationBuilder.CreateTable(
                name: "AccountReactivationRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IdentityId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SettledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountReactivationRequests", x => x.Id);
                    table.CheckConstraint("CK_AccountReactivationRequests_Ids_NotEmpty", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"IdentityId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_AccountReactivationRequests_Lifecycle", "\"Version\" > 0 AND \"ExpiresAt\" > \"IssuedAt\" AND (\"Status\" = 'Pending') = (\"SettledAt\" IS NULL) AND \"TokenHash\" ~ '^v1:[A-Za-z0-9+/]{42}[AEIMQUYcgkosw048]=$'");
                    table.ForeignKey(
                        name: "FK_AccountReactivationRequests_AspNetUsers_IdentityId",
                        column: x => x.IdentityId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountReactivationRequests_IdentityId",
                table: "AccountReactivationRequests",
                column: "IdentityId",
                unique: true,
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_AccountReactivationRequests_TokenHash",
                table: "AccountReactivationRequests",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountReactivationRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AspNetUsers_Status",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "AspNetUsers");
        }
    }
}
