using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanArchitecture.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PlatformOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Tenants that predate these columns have no true creation time. They are backfilled with the
            // moment the columns appeared, which is the earliest instant the system can actually vouch for;
            // the audit trail remains the record of what happened before it.
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "Tenants",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SuspendedAt",
                table: "Tenants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuspensionReason",
                table: "Tenants",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "Tenants",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tenants_Suspension",
                table: "Tenants",
                sql: "((\"Status\" = 'Suspended') OR (\"SuspensionReason\" IS NULL AND \"SuspendedAt\" IS NULL)) AND ((\"SuspensionReason\" IS NULL) = (\"SuspendedAt\" IS NULL)) AND \"UpdatedAt\" >= \"CreatedAt\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Tenants_Suspension",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "SuspendedAt",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "SuspensionReason",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Tenants");
        }
    }
}
