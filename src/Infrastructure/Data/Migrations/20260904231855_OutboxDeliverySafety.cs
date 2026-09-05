using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanArchitecture.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class OutboxDeliverySafety : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FirstAttemptAt",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestFingerprint",
                table: "outbox_messages",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            // Earlier versions did not retain the first attempt. Creation is a conservative lower bound;
            // an upgrade must not restart a provider's idempotency retention window for ambiguous old work.
            migrationBuilder.Sql("""
                UPDATE outbox_messages SET "FirstAttemptAt" = "CreatedAt"
                WHERE "AttemptCount" > 0 OR "LeaseOwner" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FirstAttemptAt",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "RequestFingerprint",
                table: "outbox_messages");
        }
    }
}
