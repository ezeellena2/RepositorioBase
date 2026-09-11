using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanArchitecture.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class IdentityLanguagePreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "PlatformAdminInvitations",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "pending_registration_intents",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "pending_personal_intents",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryLanguage",
                table: "outbox_messages",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "Invitations",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreferredLanguage",
                table: "AspNetUsers",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PlatformAdminInvitations_Language",
                table: "PlatformAdminInvitations",
                sql: "\"Language\" IS NULL OR \"Language\" IN ('en', 'es')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_pending_registration_intents_Language",
                table: "pending_registration_intents",
                sql: "\"Language\" IS NULL OR \"Language\" IN ('en', 'es')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_pending_personal_intents_Language",
                table: "pending_personal_intents",
                sql: "\"Language\" IS NULL OR \"Language\" IN ('en', 'es')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_outbox_messages_DeliveryLanguage",
                table: "outbox_messages",
                sql: "\"DeliveryLanguage\" IS NULL OR \"DeliveryLanguage\" IN ('en', 'es')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invitations_Language",
                table: "Invitations",
                sql: "\"Language\" IS NULL OR \"Language\" IN ('en', 'es')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AspNetUsers_PreferredLanguage",
                table: "AspNetUsers",
                sql: "\"PreferredLanguage\" IS NULL OR \"PreferredLanguage\" IN ('en', 'es')");

            // Phase 4 replaces the lifecycle template selector with one stable type per notice. This is a
            // contract-only rewrite: message ids, claim state, fingerprints, receipts and timestamps remain
            // untouched, while the durable payload becomes identifier-only (IA-REQ-029).
            migrationBuilder.Sql(
                """
                UPDATE outbox_messages
                SET "Type" = CASE "Payload" ->> 'Outcome'
                        WHEN 'self_deactivated' THEN 'identity.lifecycle.self.deactivated.notice.requested'
                        WHEN 'administratively_suspended' THEN 'identity.lifecycle.administratively.suspended.notice.requested'
                        WHEN 'reactivated' THEN 'identity.lifecycle.reactivated.notice.requested'
                    END,
                    "Payload" = jsonb_build_object('IdentityId', "Payload" -> 'IdentityId')
                WHERE "Type" = 'identity.lifecycle.notice.requested'
                  AND jsonb_typeof("Payload") = 'object'
                  AND "Payload" ? 'IdentityId'
                  AND "Payload" ->> 'Outcome' IN ('self_deactivated', 'administratively_suspended', 'reactivated');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE outbox_messages
                SET "Payload" = jsonb_build_object(
                        'IdentityId', "Payload" -> 'IdentityId',
                        'Outcome', CASE "Type"
                            WHEN 'identity.lifecycle.self.deactivated.notice.requested' THEN 'self_deactivated'
                            WHEN 'identity.lifecycle.administratively.suspended.notice.requested' THEN 'administratively_suspended'
                            WHEN 'identity.lifecycle.reactivated.notice.requested' THEN 'reactivated'
                        END),
                    "Type" = 'identity.lifecycle.notice.requested'
                WHERE "Type" IN (
                        'identity.lifecycle.self.deactivated.notice.requested',
                        'identity.lifecycle.administratively.suspended.notice.requested',
                        'identity.lifecycle.reactivated.notice.requested')
                  AND jsonb_typeof("Payload") = 'object'
                  AND "Payload" ? 'IdentityId';
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_PlatformAdminInvitations_Language",
                table: "PlatformAdminInvitations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_pending_registration_intents_Language",
                table: "pending_registration_intents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_pending_personal_intents_Language",
                table: "pending_personal_intents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_outbox_messages_DeliveryLanguage",
                table: "outbox_messages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Invitations_Language",
                table: "Invitations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AspNetUsers_PreferredLanguage",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "PlatformAdminInvitations");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "pending_registration_intents");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "pending_personal_intents");

            migrationBuilder.DropColumn(
                name: "DeliveryLanguage",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "Invitations");

            migrationBuilder.DropColumn(
                name: "PreferredLanguage",
                table: "AspNetUsers");
        }
    }
}
