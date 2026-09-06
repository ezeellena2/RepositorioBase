using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanArchitecture.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExternalProviderLinkColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Handle",
                table: "AspNetUserLogins",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValueSql: "translate(rtrim(encode(uuid_send(gen_random_uuid()), 'base64'), '='), '+/', '-_')");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LinkedAt",
                table: "AspNetUserLogins",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.CreateIndex(
                name: "UX_AspNetUserLogins_LoginProvider_UserId",
                table: "AspNetUserLogins",
                columns: new[] { "LoginProvider", "UserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_AspNetUserLogins_LoginProvider_UserId",
                table: "AspNetUserLogins");

            migrationBuilder.DropColumn(
                name: "Handle",
                table: "AspNetUserLogins");

            migrationBuilder.DropColumn(
                name: "LinkedAt",
                table: "AspNetUserLogins");
        }
    }
}
