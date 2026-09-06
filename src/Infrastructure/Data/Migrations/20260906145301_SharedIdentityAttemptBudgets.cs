using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanArchitecture.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SharedIdentityAttemptBudgets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IdentityAttemptBudgets",
                columns: table => new
                {
                    Scope = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    KeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WindowStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentityAttemptBudgets", x => new { x.Scope, x.KeyHash, x.WindowStart });
                    table.CheckConstraint("CK_IdentityAttemptBudgets_Window", "\"Count\" >= 0 AND \"ExpiresAt\" > \"WindowStart\" AND \"Scope\" <> '' AND \"KeyHash\" <> ''");
                });

            migrationBuilder.CreateIndex(
                name: "IX_IdentityAttemptBudgets_ExpiresAt",
                table: "IdentityAttemptBudgets",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IdentityAttemptBudgets");
        }
    }
}
