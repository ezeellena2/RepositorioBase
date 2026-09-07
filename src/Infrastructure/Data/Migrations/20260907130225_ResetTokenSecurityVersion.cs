using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanArchitecture.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ResetTokenSecurityVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PasswordResetRequests_Lifecycle",
                table: "PasswordResetRequests");

            migrationBuilder.AddColumn<long>(
                name: "SecurityVersion",
                table: "PasswordResetRequests",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PasswordResetRequests_Lifecycle",
                table: "PasswordResetRequests",
                sql: "\"Version\" > 0 AND \"SecurityVersion\" >= 0 AND \"ExpiresAt\" > \"IssuedAt\" AND (\"Status\" = 'Pending') = (\"SettledAt\" IS NULL) AND \"TokenHash\" ~ '^v1:[A-Za-z0-9+/]{42}[AEIMQUYcgkosw048]=$'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PasswordResetRequests_Lifecycle",
                table: "PasswordResetRequests");

            migrationBuilder.DropColumn(
                name: "SecurityVersion",
                table: "PasswordResetRequests");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PasswordResetRequests_Lifecycle",
                table: "PasswordResetRequests",
                sql: "\"Version\" > 0 AND \"ExpiresAt\" > \"IssuedAt\" AND (\"Status\" = 'Pending') = (\"SettledAt\" IS NULL) AND \"TokenHash\" ~ '^v1:[A-Za-z0-9+/]{42}[AEIMQUYcgkosw048]=$'");
        }
    }
}
