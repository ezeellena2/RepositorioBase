using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanArchitecture.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class OrganizationOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OwnerMembershipId",
                table: "Tenants",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Id_OwnerMembershipId",
                table: "Tenants",
                columns: new[] { "Id", "OwnerMembershipId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Tenants_TenantMemberships_Id_OwnerMembershipId",
                table: "Tenants",
                columns: new[] { "Id", "OwnerMembershipId" },
                principalTable: "TenantMemberships",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            // Every organization that existed before ownership did gets the membership that holds its system
            // `Owner` role, which is the responsible member registration created. "An organization always has an
            // owner" is an application invariant plus this backfill, deliberately not a CHECK: the column has to
            // be nullable for the instant between inserting a tenant and inserting the membership it points at.
            // Only where exactly one such membership exists, so an ambiguous tenant is left for a person to look
            // at rather than being given an owner by a coin toss.
            migrationBuilder.Sql("""
                UPDATE "Tenants" AS t
                SET "OwnerMembershipId" = candidate."MembershipId"
                FROM (
                    -- Compared as text because PostgreSQL defines no MIN over uuid. Any deterministic pick
                    -- would do; what matters is that the same tenant always resolves to the same membership.
                    SELECT m."TenantId", MIN(m."Id"::text)::uuid AS "MembershipId", COUNT(*) AS "Holders"
                    FROM "TenantMemberships" AS m
                    JOIN "MembershipRoles" AS mr ON mr."TenantId" = m."TenantId" AND mr."MembershipId" = m."Id"
                    JOIN "Roles" AS r ON r."TenantId" = mr."TenantId" AND r."Id" = mr."RoleId"
                    WHERE r."IsSystem" AND NOT r."IsRetired" AND m."Status" = 'Active'
                    GROUP BY m."TenantId"
                ) AS candidate
                WHERE t."Id" = candidate."TenantId"
                  AND t."Type" = 'Organization'
                  AND t."OwnerMembershipId" IS NULL
                  AND candidate."Holders" = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tenants_TenantMemberships_Id_OwnerMembershipId",
                table: "Tenants");

            migrationBuilder.DropIndex(
                name: "IX_Tenants_Id_OwnerMembershipId",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "OwnerMembershipId",
                table: "Tenants");
        }
    }
}
