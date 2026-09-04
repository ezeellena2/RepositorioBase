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
            // An applied database is holding rows the constraint below would reject: the previous one pinned only
            // the "v<n>:" prefix, banned ASCII uppercase alone, and trimmed only the ends of a recipient. Adding a
            // constraint over such a row fails, and the deployment fails with it, so they are carried across first.
            // No row is deleted: an invitation is history (IA-REQ-036).

            // A hash the application cannot read is a hash no token can be checked against, so the invitation is
            // retired rather than repaired -- its digest is unrecoverable by construction. The replacement is
            // random rather than derived from the row, so it is not a digest of anything a caller could submit.
            migrationBuilder.Sql(
                """
                UPDATE "Invitations"
                SET "TokenHash" = 'v1:' || encode(sha256(gen_random_uuid()::text::bytea), 'base64'),
                    "Status" = CASE WHEN "Status" = 'Pending' THEN 'Cancelled' ELSE "Status" END,
                    "CancelledAt" = CASE WHEN "Status" = 'Pending' THEN GREATEST("CreatedAt", NOW()) ELSE "CancelledAt" END
                WHERE "TokenHash" !~ '^v1:[A-Za-z0-9+/]{43}=$';
                """);

            // Folding case and stripping whitespace can make two rows name one recipient, which the pending
            // uniqueness index forbids. The earliest invitation keeps the slot; the rest are cancelled.
            migrationBuilder.Sql(
                """
                WITH repaired AS (
                    SELECT "Id",
                           row_number() OVER (
                               PARTITION BY "TenantId", regexp_replace(lower("NormalizedEmail"), '[[:space:]]|' || U&'\00a0', '', 'g')
                               ORDER BY "CreatedAt", "Id") AS slot
                    FROM "Invitations"
                    WHERE "Status" = 'Pending')
                UPDATE "Invitations" AS invitation
                SET "Status" = 'Cancelled', "CancelledAt" = GREATEST(invitation."CreatedAt", NOW())
                FROM repaired
                WHERE invitation."Id" = repaired."Id" AND repaired.slot > 1;
                """);

            migrationBuilder.Sql(
                """
                UPDATE "Invitations"
                SET "NormalizedEmail" = regexp_replace(lower("NormalizedEmail"), '[[:space:]]|' || U&'\00a0', '', 'g')
                WHERE "NormalizedEmail" <> regexp_replace(lower("NormalizedEmail"), '[[:space:]]|' || U&'\00a0', '', 'g');
                """);

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
