using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanArchitecture.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class OutboxDispatchState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeliveredAt",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Generation",
                table: "outbox_messages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeaseExpiresAt",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LeaseOwner",
                table: "outbox_messages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "outbox_messages",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                // An applied database is holding messages that were written before dispatch had any state at all.
                // Every one of them is still waiting to be sent, so the default backfills them as Pending: the
                // empty string EF would otherwise use is not a status, and the constraint below rejects it.
                defaultValue: "Pending");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_Status_NextAttemptAt",
                table: "outbox_messages",
                columns: new[] { "Status", "NextAttemptAt" },
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_outbox_messages_Dispatch",
                table: "outbox_messages",
                sql: "\"AttemptCount\" >= 0 AND \"Generation\" >= 0 AND ((\"LeaseOwner\" IS NULL) = (\"LeaseExpiresAt\" IS NULL)) AND ((\"Status\" = 'Pending' AND \"DeliveredAt\" IS NULL) OR (\"Status\" = 'Delivered' AND \"DeliveredAt\" IS NOT NULL AND \"LeaseOwner\" IS NULL) OR (\"Status\" = 'Abandoned' AND \"DeliveredAt\" IS NULL AND \"LeaseOwner\" IS NULL AND \"FailureCode\" IS NOT NULL))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_outbox_messages_Status_NextAttemptAt",
                table: "outbox_messages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_outbox_messages_Dispatch",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "DeliveredAt",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "Generation",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAt",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "LeaseOwner",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "outbox_messages");
        }
    }
}
