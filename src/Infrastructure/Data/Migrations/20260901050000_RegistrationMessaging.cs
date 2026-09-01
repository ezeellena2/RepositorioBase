using System;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanArchitecture.Infrastructure.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260901050000_RegistrationMessaging")]
public partial class RegistrationMessaging : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "outbox_messages",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Payload = table.Column<string>(type: "jsonb", nullable: false),
                AttemptCount = table.Column<int>(type: "integer", nullable: false),
                NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                FailureCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_outbox_messages", x => x.Id);
                table.CheckConstraint("CK_outbox_messages_Id_NotEmpty", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            });
        migrationBuilder.CreateTable(
            name: "registration_submissions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CanonicalKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                Outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_registration_submissions", x => x.Id);
                table.CheckConstraint("CK_registration_submissions_Id_NotEmpty", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            });
        migrationBuilder.CreateTable(
            name: "outbox_secrets",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OutboxMessageId = table.Column<Guid>(type: "uuid", nullable: false),
                VersionedHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Ciphertext = table.Column<string>(type: "text", nullable: true),
                ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                TerminalReason = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                DeliveryReason = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                ProviderReceipt = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                DeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_outbox_secrets", x => x.Id);
                table.CheckConstraint("CK_outbox_secrets_Ids_NotEmpty", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"OutboxMessageId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.CheckConstraint("CK_outbox_secrets_Lifecycle", "(\"Status\" = 'Pending' AND \"Ciphertext\" IS NOT NULL AND \"DeliveryReason\" IS NULL AND \"DeliveredAt\" IS NULL AND \"TerminalReason\" IS NULL AND \"CompletedAt\" IS NULL AND \"ProviderReceipt\" IS NULL) OR (\"Status\" = 'Delivered' AND \"Ciphertext\" IS NULL AND \"DeliveryReason\" IS NOT NULL AND \"DeliveredAt\" IS NOT NULL AND \"TerminalReason\" IS NULL AND \"CompletedAt\" IS NULL AND \"ProviderReceipt\" IS NOT NULL) OR (\"Status\" = 'Consumed' AND \"Ciphertext\" IS NULL AND \"TerminalReason\" IS NOT NULL AND \"CompletedAt\" IS NOT NULL AND ((\"DeliveryReason\" IS NULL AND \"DeliveredAt\" IS NULL AND \"ProviderReceipt\" IS NULL) OR (\"DeliveryReason\" IS NOT NULL AND \"DeliveredAt\" IS NOT NULL AND \"ProviderReceipt\" IS NOT NULL))) OR (\"Status\" IN ('Expired', 'Failed') AND \"Ciphertext\" IS NULL AND \"TerminalReason\" IS NOT NULL AND \"CompletedAt\" IS NOT NULL AND ((\"DeliveryReason\" IS NULL AND \"DeliveredAt\" IS NULL AND \"ProviderReceipt\" IS NULL) OR (\"DeliveryReason\" IS NOT NULL AND \"DeliveredAt\" IS NOT NULL AND \"ProviderReceipt\" IS NOT NULL)))");
                table.ForeignKey(
                    name: "FK_outbox_secrets_outbox_messages_OutboxMessageId",
                    column: x => x.OutboxMessageId,
                    principalTable: "outbox_messages",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex(name: "IX_outbox_messages_NextAttemptAt", table: "outbox_messages", column: "NextAttemptAt");
        migrationBuilder.CreateIndex(name: "IX_outbox_secrets_OutboxMessageId", table: "outbox_secrets", column: "OutboxMessageId", unique: true);
        migrationBuilder.CreateIndex(name: "IX_outbox_secrets_VersionedHash", table: "outbox_secrets", column: "VersionedHash", unique: true);
        migrationBuilder.CreateIndex(name: "IX_registration_submissions_CanonicalKey", table: "registration_submissions", column: "CanonicalKey", unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "outbox_secrets");
        migrationBuilder.DropTable(name: "registration_submissions");
        migrationBuilder.DropTable(name: "outbox_messages");
    }
}
