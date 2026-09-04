using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanArchitecture.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InvitationCanonicalTokenEncoding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The tightened clause admits only canonical Base64, and an applied database may hold a digest whose
            // final character carries bits the payload does not use. It cannot be repaired — a non-canonical
            // encoding names a digest whose preimage is unknown — so the row is retired the way the earlier
            // canonicalization retired unreadable hashes: the invitation survives as history, unusable.
            //
            // The trigger is lifted for the duration because a retired row may already be settled, and restored
            // immediately afterwards.
            migrationBuilder.Sql("ALTER TABLE \"Invitations\" DISABLE TRIGGER \"TR_Invitations_PreventSettledChange\";");

            migrationBuilder.Sql(
                """
                UPDATE "Invitations"
                SET "TokenHash" = 'v1:' || encode(sha256(gen_random_uuid()::text::bytea), 'base64'),
                    "Status" = CASE WHEN "Status" = 'Pending' THEN 'Cancelled' ELSE "Status" END,
                    "CancelledAt" = CASE WHEN "Status" = 'Pending' THEN GREATEST("CreatedAt", NOW()) ELSE "CancelledAt" END
                WHERE "TokenHash" !~ '^v1:[A-Za-z0-9+/]{42}[AEIMQUYcgkosw048]=$';
                """);

            migrationBuilder.Sql("ALTER TABLE \"Invitations\" ENABLE TRIGGER \"TR_Invitations_PreventSettledChange\";");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Invitations_Lifecycle",
                table: "Invitations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invitations_Lifecycle",
                table: "Invitations",
                sql: "\"TokenHash\" ~ '^v1:[A-Za-z0-9+/]{42}[AEIMQUYcgkosw048]=$' AND \"NormalizedEmail\" = normalize(\"NormalizedEmail\", NFC) AND \"NormalizedEmail\" = lower(\"NormalizedEmail\") AND \"NormalizedEmail\" !~ '[[:space:]]' AND position(U&'\\00a0' IN \"NormalizedEmail\") = 0 AND strpos(\"NormalizedEmail\", '@') > 0 AND \"ExpiresAt\" > \"CreatedAt\" AND ((\"Status\" = 'Pending' AND \"AcceptedByIdentityId\" IS NULL AND \"AcceptedAt\" IS NULL AND \"CancelledAt\" IS NULL) OR (\"Status\" = 'Accepted' AND \"AcceptedByIdentityId\" IS NOT NULL AND \"AcceptedAt\" IS NOT NULL AND \"AcceptedAt\" >= \"CreatedAt\" AND \"AcceptedAt\" <= \"ExpiresAt\" AND \"CancelledAt\" IS NULL) OR (\"Status\" = 'Cancelled' AND \"AcceptedByIdentityId\" IS NULL AND \"AcceptedAt\" IS NULL AND \"CancelledAt\" IS NOT NULL AND \"CancelledAt\" >= \"CreatedAt\"))");
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
                sql: "\"TokenHash\" ~ '^v1:[A-Za-z0-9+/]{43}=$' AND \"NormalizedEmail\" = normalize(\"NormalizedEmail\", NFC) AND \"NormalizedEmail\" = lower(\"NormalizedEmail\") AND \"NormalizedEmail\" !~ '[[:space:]]' AND position(U&'\\00a0' IN \"NormalizedEmail\") = 0 AND strpos(\"NormalizedEmail\", '@') > 0 AND \"ExpiresAt\" > \"CreatedAt\" AND ((\"Status\" = 'Pending' AND \"AcceptedByIdentityId\" IS NULL AND \"AcceptedAt\" IS NULL AND \"CancelledAt\" IS NULL) OR (\"Status\" = 'Accepted' AND \"AcceptedByIdentityId\" IS NOT NULL AND \"AcceptedAt\" IS NOT NULL AND \"AcceptedAt\" >= \"CreatedAt\" AND \"AcceptedAt\" <= \"ExpiresAt\" AND \"CancelledAt\" IS NULL) OR (\"Status\" = 'Cancelled' AND \"AcceptedByIdentityId\" IS NULL AND \"AcceptedAt\" IS NULL AND \"CancelledAt\" IS NOT NULL AND \"CancelledAt\" >= \"CreatedAt\"))");
        }
    }
}
