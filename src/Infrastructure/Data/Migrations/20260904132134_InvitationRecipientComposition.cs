using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanArchitecture.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InvitationRecipientComposition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every statement below rewrites rows this table's own trigger freezes once they settle, and the
            // canonicalization pass also touches the losers it just cancelled. The trigger is therefore lifted for
            // the duration of the migration and restored immediately after: a data migration is precisely the
            // operation that must reach settled history, and leaving it in place aborts the upgrade instead.
            migrationBuilder.Sql("ALTER TABLE \"Invitations\" DISABLE TRIGGER \"TR_Invitations_PreventSettledChange\";");

            // Composition is new, so an applied database may hold a recipient spelled with combining marks. As in
            // the migration before it, composing can collide with a row already holding the pending slot for that
            // recipient; the earliest invitation keeps it and the rest are cancelled rather than deleted.
            migrationBuilder.Sql(
                """
                WITH composed AS (
                    SELECT "Id",
                           row_number() OVER (
                               PARTITION BY "TenantId", normalize("NormalizedEmail", NFC)
                               ORDER BY "CreatedAt", "Id") AS slot
                    FROM "Invitations"
                    WHERE "Status" = 'Pending')
                UPDATE "Invitations" AS invitation
                SET "Status" = 'Cancelled', "CancelledAt" = GREATEST(invitation."CreatedAt", NOW())
                FROM composed
                WHERE invitation."Id" = composed."Id" AND composed.slot > 1;
                """);

            migrationBuilder.Sql(
                """
                UPDATE "Invitations"
                SET "NormalizedEmail" = normalize("NormalizedEmail", NFC)
                WHERE "NormalizedEmail" <> normalize("NormalizedEmail", NFC);
                """);

            migrationBuilder.Sql("ALTER TABLE \"Invitations\" ENABLE TRIGGER \"TR_Invitations_PreventSettledChange\";");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Invitations_Lifecycle",
                table: "Invitations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invitations_Lifecycle",
                table: "Invitations",
                sql: "\"TokenHash\" ~ '^v1:[A-Za-z0-9+/]{43}=$' AND \"NormalizedEmail\" = normalize(\"NormalizedEmail\", NFC) AND \"NormalizedEmail\" = lower(\"NormalizedEmail\") AND \"NormalizedEmail\" !~ '[[:space:]]' AND position(U&'\\00a0' IN \"NormalizedEmail\") = 0 AND strpos(\"NormalizedEmail\", '@') > 0 AND \"ExpiresAt\" > \"CreatedAt\" AND ((\"Status\" = 'Pending' AND \"AcceptedByIdentityId\" IS NULL AND \"AcceptedAt\" IS NULL AND \"CancelledAt\" IS NULL) OR (\"Status\" = 'Accepted' AND \"AcceptedByIdentityId\" IS NOT NULL AND \"AcceptedAt\" IS NOT NULL AND \"AcceptedAt\" >= \"CreatedAt\" AND \"AcceptedAt\" <= \"ExpiresAt\" AND \"CancelledAt\" IS NULL) OR (\"Status\" = 'Cancelled' AND \"AcceptedByIdentityId\" IS NULL AND \"AcceptedAt\" IS NULL AND \"CancelledAt\" IS NOT NULL AND \"CancelledAt\" >= \"CreatedAt\"))");
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
                sql: "\"TokenHash\" ~ '^v1:[A-Za-z0-9+/]{43}=$' AND \"NormalizedEmail\" = lower(\"NormalizedEmail\") AND \"NormalizedEmail\" !~ '[[:space:]]' AND position(U&'\\00a0' IN \"NormalizedEmail\") = 0 AND strpos(\"NormalizedEmail\", '@') > 0 AND \"ExpiresAt\" > \"CreatedAt\" AND ((\"Status\" = 'Pending' AND \"AcceptedByIdentityId\" IS NULL AND \"AcceptedAt\" IS NULL AND \"CancelledAt\" IS NULL) OR (\"Status\" = 'Accepted' AND \"AcceptedByIdentityId\" IS NOT NULL AND \"AcceptedAt\" IS NOT NULL AND \"AcceptedAt\" >= \"CreatedAt\" AND \"AcceptedAt\" <= \"ExpiresAt\" AND \"CancelledAt\" IS NULL) OR (\"Status\" = 'Cancelled' AND \"AcceptedByIdentityId\" IS NULL AND \"AcceptedAt\" IS NULL AND \"CancelledAt\" IS NOT NULL AND \"CancelledAt\" >= \"CreatedAt\"))");
        }
    }
}
