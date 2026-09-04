using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanArchitecture.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InvitationCanonicalForm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Invitations_Lifecycle",
                table: "Invitations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invitations_Lifecycle",
                table: "Invitations",
                sql: "\"TokenHash\" ~ '^v1:[A-Za-z0-9+/]{43}=$' AND \"NormalizedEmail\" = lower(\"NormalizedEmail\") AND \"NormalizedEmail\" !~ '[[:space:]]' AND position(U&'\\00a0' IN \"NormalizedEmail\") = 0 AND strpos(\"NormalizedEmail\", '@') > 0 AND \"ExpiresAt\" > \"CreatedAt\" AND ((\"Status\" = 'Pending' AND \"AcceptedByIdentityId\" IS NULL AND \"AcceptedAt\" IS NULL AND \"CancelledAt\" IS NULL) OR (\"Status\" = 'Accepted' AND \"AcceptedByIdentityId\" IS NOT NULL AND \"AcceptedAt\" IS NOT NULL AND \"AcceptedAt\" >= \"CreatedAt\" AND \"AcceptedAt\" <= \"ExpiresAt\" AND \"CancelledAt\" IS NULL) OR (\"Status\" = 'Cancelled' AND \"AcceptedByIdentityId\" IS NULL AND \"AcceptedAt\" IS NULL AND \"CancelledAt\" IS NOT NULL AND \"CancelledAt\" >= \"CreatedAt\"))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Invitations_Lifecycle",
                table: "Invitations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invitations_Lifecycle",
                table: "Invitations",
                sql: "\"TokenHash\" ~ '^v[0123456789]+:' AND \"NormalizedEmail\" !~ '[ABCDEFGHIJKLMNOPQRSTUVWXYZ]' AND \"NormalizedEmail\" = btrim(\"NormalizedEmail\") AND strpos(\"NormalizedEmail\", '@') > 0 AND \"ExpiresAt\" > \"CreatedAt\" AND ((\"Status\" = 'Pending' AND \"AcceptedByIdentityId\" IS NULL AND \"AcceptedAt\" IS NULL AND \"CancelledAt\" IS NULL) OR (\"Status\" = 'Accepted' AND \"AcceptedByIdentityId\" IS NOT NULL AND \"AcceptedAt\" IS NOT NULL AND \"AcceptedAt\" >= \"CreatedAt\" AND \"AcceptedAt\" <= \"ExpiresAt\" AND \"CancelledAt\" IS NULL) OR (\"Status\" = 'Cancelled' AND \"AcceptedByIdentityId\" IS NULL AND \"AcceptedAt\" IS NULL AND \"CancelledAt\" IS NOT NULL AND \"CancelledAt\" >= \"CreatedAt\"))");
        }
    }
}
