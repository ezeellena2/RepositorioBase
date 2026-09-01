using System;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanArchitecture.Infrastructure.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260901024500_AuthorizationDenialAudit")]
public partial class AuthorizationDenialAudit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_AuditEvents_Ids_NotEmpty",
            table: "AuditEvents");

        migrationBuilder.AlterColumn<Guid>(
            name: "TenantId",
            table: "AuditEvents",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid");

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "OccurredAt",
            table: "AuditEvents",
            type: "timestamp with time zone",
            nullable: false,
            defaultValueSql: "CURRENT_TIMESTAMP");

        migrationBuilder.AddColumn<Guid>(
            name: "SessionId",
            table: "AuditEvents",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_AuditEvents_Ids_NotEmpty",
            table: "AuditEvents",
            sql: "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND (\"TenantId\" IS NULL OR \"TenantId\" <> '00000000-0000-0000-0000-000000000000'::uuid) AND (\"ActorId\" IS NULL OR \"ActorId\" <> '00000000-0000-0000-0000-000000000000'::uuid) AND (\"SessionId\" IS NULL OR \"SessionId\" <> '00000000-0000-0000-0000-000000000000'::uuid)");

        migrationBuilder.CreateIndex(
            name: "IX_AuditEvents_SessionId",
            table: "AuditEvents",
            column: "SessionId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_AuditEvents_SessionId", table: "AuditEvents");
        migrationBuilder.DropCheckConstraint(name: "CK_AuditEvents_Ids_NotEmpty", table: "AuditEvents");
        migrationBuilder.DropColumn(name: "OccurredAt", table: "AuditEvents");
        migrationBuilder.DropColumn(name: "SessionId", table: "AuditEvents");

        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS "TR_AuditEvents_AppendOnly" ON public."AuditEvents";
            DROP TRIGGER IF EXISTS "TR_AuditEvents_AppendOnlyTruncate" ON public."AuditEvents";
            DELETE FROM "AuditEvents" WHERE "TenantId" IS NULL;
            CREATE TRIGGER "TR_AuditEvents_AppendOnly" BEFORE UPDATE OR DELETE ON public."AuditEvents" FOR EACH ROW EXECUTE FUNCTION public.prevent_audit_event_mutation();
            CREATE TRIGGER "TR_AuditEvents_AppendOnlyTruncate" BEFORE TRUNCATE ON public."AuditEvents" FOR EACH STATEMENT EXECUTE FUNCTION public.prevent_audit_event_mutation();
            """);
        migrationBuilder.AlterColumn<Guid>(
            name: "TenantId",
            table: "AuditEvents",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_AuditEvents_Ids_NotEmpty",
            table: "AuditEvents",
            sql: "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"TenantId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND (\"ActorId\" IS NULL OR \"ActorId\" <> '00000000-0000-0000-0000-000000000000'::uuid)");
    }
}
