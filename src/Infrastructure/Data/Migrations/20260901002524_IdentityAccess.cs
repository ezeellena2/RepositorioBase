using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CleanArchitecture.Infrastructure.Data.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260901002524_IdentityAccess")]
    public partial class IdentityAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "EmailIndex",
                table: "AspNetUsers");

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    BEGIN PERFORM "Id"::uuid FROM "AspNetUsers"; EXCEPTION WHEN invalid_text_representation THEN RAISE EXCEPTION 'IdentityAccess requires every AspNetUsers.Id to be a parseable UUID'; END;
                    BEGIN PERFORM "Id"::uuid FROM "AspNetRoles"; EXCEPTION WHEN invalid_text_representation THEN RAISE EXCEPTION 'IdentityAccess requires every AspNetRoles.Id to be a parseable UUID'; END;
                    BEGIN PERFORM "RoleId"::uuid FROM "AspNetRoleClaims"; EXCEPTION WHEN invalid_text_representation THEN RAISE EXCEPTION 'IdentityAccess requires every AspNetRoleClaims.RoleId to be a parseable UUID'; END;
                    BEGIN PERFORM "UserId"::uuid FROM "AspNetUserClaims"; EXCEPTION WHEN invalid_text_representation THEN RAISE EXCEPTION 'IdentityAccess requires every AspNetUserClaims.UserId to be a parseable UUID'; END;
                    BEGIN PERFORM "UserId"::uuid FROM "AspNetUserLogins"; EXCEPTION WHEN invalid_text_representation THEN RAISE EXCEPTION 'IdentityAccess requires every AspNetUserLogins.UserId to be a parseable UUID'; END;
                    BEGIN PERFORM "UserId"::uuid, "RoleId"::uuid FROM "AspNetUserRoles"; EXCEPTION WHEN invalid_text_representation THEN RAISE EXCEPTION 'IdentityAccess requires every AspNetUserRoles key to be a parseable UUID'; END;
                    BEGIN PERFORM "UserId"::uuid FROM "AspNetUserTokens"; EXCEPTION WHEN invalid_text_representation THEN RAISE EXCEPTION 'IdentityAccess requires every AspNetUserTokens.UserId to be a parseable UUID'; END;
                    BEGIN PERFORM "CreatedBy"::uuid, "LastModifiedBy"::uuid FROM "TodoLists" WHERE "CreatedBy" IS NOT NULL OR "LastModifiedBy" IS NOT NULL; EXCEPTION WHEN invalid_text_representation THEN RAISE EXCEPTION 'IdentityAccess requires non-null TodoLists audit actors to be parseable UUIDs'; END;
                    BEGIN PERFORM "CreatedBy"::uuid, "LastModifiedBy"::uuid FROM "TodoItems" WHERE "CreatedBy" IS NOT NULL OR "LastModifiedBy" IS NOT NULL; EXCEPTION WHEN invalid_text_representation THEN RAISE EXCEPTION 'IdentityAccess requires non-null TodoItems audit actors to be parseable UUIDs'; END;
                    IF EXISTS (SELECT 1 FROM "AspNetUsers" WHERE "Id"::uuid = '00000000-0000-0000-0000-000000000000'::uuid) OR EXISTS (SELECT 1 FROM "AspNetRoles" WHERE "Id"::uuid = '00000000-0000-0000-0000-000000000000'::uuid) THEN
                        RAISE EXCEPTION 'IdentityAccess does not permit an all-zero identity key';
                    END IF;
                    IF EXISTS (SELECT 1 FROM "AspNetUsers" WHERE "NormalizedEmail" IS NOT NULL GROUP BY "NormalizedEmail" HAVING COUNT(*) > 1) THEN
                        RAISE EXCEPTION 'IdentityAccess requires unique non-null normalized email values';
                    END IF;
                END $$;

                ALTER TABLE "AspNetRoleClaims" DROP CONSTRAINT "FK_AspNetRoleClaims_AspNetRoles_RoleId";
                ALTER TABLE "AspNetUserClaims" DROP CONSTRAINT "FK_AspNetUserClaims_AspNetUsers_UserId";
                ALTER TABLE "AspNetUserLogins" DROP CONSTRAINT "FK_AspNetUserLogins_AspNetUsers_UserId";
                ALTER TABLE "AspNetUserRoles" DROP CONSTRAINT "FK_AspNetUserRoles_AspNetRoles_RoleId";
                ALTER TABLE "AspNetUserRoles" DROP CONSTRAINT "FK_AspNetUserRoles_AspNetUsers_UserId";
                ALTER TABLE "AspNetUserTokens" DROP CONSTRAINT "FK_AspNetUserTokens_AspNetUsers_UserId";

                ALTER TABLE "AspNetRoles" ALTER COLUMN "Id" TYPE uuid USING "Id"::uuid;
                ALTER TABLE "AspNetUsers" ALTER COLUMN "Id" TYPE uuid USING "Id"::uuid;
                ALTER TABLE "AspNetRoleClaims" ALTER COLUMN "RoleId" TYPE uuid USING "RoleId"::uuid;
                ALTER TABLE "AspNetUserClaims" ALTER COLUMN "UserId" TYPE uuid USING "UserId"::uuid;
                ALTER TABLE "AspNetUserLogins" ALTER COLUMN "UserId" TYPE uuid USING "UserId"::uuid;
                ALTER TABLE "AspNetUserRoles" ALTER COLUMN "UserId" TYPE uuid USING "UserId"::uuid;
                ALTER TABLE "AspNetUserRoles" ALTER COLUMN "RoleId" TYPE uuid USING "RoleId"::uuid;
                ALTER TABLE "AspNetUserTokens" ALTER COLUMN "UserId" TYPE uuid USING "UserId"::uuid;
                ALTER TABLE "TodoLists" ALTER COLUMN "CreatedBy" TYPE uuid USING "CreatedBy"::uuid;
                ALTER TABLE "TodoLists" ALTER COLUMN "LastModifiedBy" TYPE uuid USING "LastModifiedBy"::uuid;
                ALTER TABLE "TodoItems" ALTER COLUMN "CreatedBy" TYPE uuid USING "CreatedBy"::uuid;
                ALTER TABLE "TodoItems" ALTER COLUMN "LastModifiedBy" TYPE uuid USING "LastModifiedBy"::uuid;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "LastModifiedBy",
                table: "TodoLists",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "CreatedBy",
                table: "TodoLists",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "LastModifiedBy",
                table: "TodoItems",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "CreatedBy",
                table: "TodoItems",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "AspNetUserTokens",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "AspNetUsers",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<Guid>(
                name: "RoleId",
                table: "AspNetUserRoles",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "AspNetUserRoles",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "AspNetUserLogins",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "AspNetUserClaims",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "AspNetRoles",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<Guid>(
                name: "RoleId",
                table: "AspNetRoleClaims",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AspNetUsers_Id_NotEmpty",
                table: "AspNetUsers",
                sql: "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AspNetRoles_Id_NotEmpty",
                table: "AspNetRoles",
                sql: "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TodoLists_AuditActors_NotEmpty",
                table: "TodoLists",
                sql: "(\"CreatedBy\" IS NULL OR \"CreatedBy\" <> '00000000-0000-0000-0000-000000000000'::uuid) AND (\"LastModifiedBy\" IS NULL OR \"LastModifiedBy\" <> '00000000-0000-0000-0000-000000000000'::uuid)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TodoItems_AuditActors_NotEmpty",
                table: "TodoItems",
                sql: "(\"CreatedBy\" IS NULL OR \"CreatedBy\" <> '00000000-0000-0000-0000-000000000000'::uuid) AND (\"LastModifiedBy\" IS NULL OR \"LastModifiedBy\" <> '00000000-0000-0000-0000-000000000000'::uuid)");

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AuthorizationVersion = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                    table.CheckConstraint("CK_Tenants_Id_NotEmpty", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                });

            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    EventType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Metadata = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                    table.CheckConstraint("CK_AuditEvents_Ids_NotEmpty", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"TenantId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND (\"ActorId\" IS NULL OR \"ActorId\" <> '00000000-0000-0000-0000-000000000000'::uuid)");
                    table.ForeignKey(
                        name: "FK_AuditEvents_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AuditEvents_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OrganizationProfiles",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    LegalName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Cuit = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationProfiles", x => x.TenantId);
                    table.CheckConstraint("CK_OrganizationProfiles_TenantId_NotEmpty", "\"TenantId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_OrganizationProfiles_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TenantMemberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdentityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantMemberships", x => x.Id);
                    table.UniqueConstraint("AK_TenantMemberships_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_TenantMemberships_Ids_NotEmpty", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"TenantId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"IdentityId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_TenantMemberships_AspNetUsers_IdentityId",
                        column: x => x.IdentityId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TenantMemberships_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_ActorId",
                table: "AuditEvents",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_TenantId",
                table: "AuditEvents",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationProfiles_Cuit",
                table: "OrganizationProfiles",
                column: "Cuit",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantMemberships_IdentityId",
                table: "TenantMemberships",
                column: "IdentityId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantMemberships_TenantId_IdentityId",
                table: "TenantMemberships",
                columns: new[] { "TenantId", "IdentityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Slug",
                table: "Tenants",
                column: "Slug",
                unique: true);

            migrationBuilder.Sql("""
                ALTER TABLE "AspNetRoleClaims" ADD CONSTRAINT "FK_AspNetRoleClaims_AspNetRoles_RoleId" FOREIGN KEY ("RoleId") REFERENCES "AspNetRoles" ("Id") ON DELETE CASCADE;
                ALTER TABLE "AspNetUserClaims" ADD CONSTRAINT "FK_AspNetUserClaims_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE;
                ALTER TABLE "AspNetUserLogins" ADD CONSTRAINT "FK_AspNetUserLogins_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE;
                ALTER TABLE "AspNetUserRoles" ADD CONSTRAINT "FK_AspNetUserRoles_AspNetRoles_RoleId" FOREIGN KEY ("RoleId") REFERENCES "AspNetRoles" ("Id") ON DELETE CASCADE;
                ALTER TABLE "AspNetUserRoles" ADD CONSTRAINT "FK_AspNetUserRoles_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE;
                ALTER TABLE "AspNetUserTokens" ADD CONSTRAINT "FK_AspNetUserTokens_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE;

                CREATE FUNCTION public.prevent_audit_event_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'AuditEvents are append-only';
                END;
                $$;
                CREATE TRIGGER "TR_AuditEvents_AppendOnly" BEFORE UPDATE OR DELETE ON public."AuditEvents" FOR EACH ROW EXECUTE FUNCTION public.prevent_audit_event_mutation();
                CREATE TRIGGER "TR_AuditEvents_AppendOnlyTruncate" BEFORE TRUNCATE ON public."AuditEvents" FOR EACH STATEMENT EXECUTE FUNCTION public.prevent_audit_event_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS \"TR_AuditEvents_AppendOnly\" ON public.\"AuditEvents\"; DROP TRIGGER IF EXISTS \"TR_AuditEvents_AppendOnlyTruncate\" ON public.\"AuditEvents\";");

            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS public.prevent_audit_event_mutation();");

            migrationBuilder.DropTable(
                name: "OrganizationProfiles");

            migrationBuilder.DropTable(
                name: "TenantMemberships");

            migrationBuilder.DropTable(
                name: "Tenants");

            migrationBuilder.DropIndex(
                name: "EmailIndex",
                table: "AspNetUsers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TodoItems_AuditActors_NotEmpty",
                table: "TodoItems");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TodoLists_AuditActors_NotEmpty",
                table: "TodoLists");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AspNetUsers_Id_NotEmpty",
                table: "AspNetUsers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AspNetRoles_Id_NotEmpty",
                table: "AspNetRoles");

            migrationBuilder.Sql("""
                ALTER TABLE "AspNetRoleClaims" DROP CONSTRAINT "FK_AspNetRoleClaims_AspNetRoles_RoleId";
                ALTER TABLE "AspNetUserClaims" DROP CONSTRAINT "FK_AspNetUserClaims_AspNetUsers_UserId";
                ALTER TABLE "AspNetUserLogins" DROP CONSTRAINT "FK_AspNetUserLogins_AspNetUsers_UserId";
                ALTER TABLE "AspNetUserRoles" DROP CONSTRAINT "FK_AspNetUserRoles_AspNetRoles_RoleId";
                ALTER TABLE "AspNetUserRoles" DROP CONSTRAINT "FK_AspNetUserRoles_AspNetUsers_UserId";
                ALTER TABLE "AspNetUserTokens" DROP CONSTRAINT "FK_AspNetUserTokens_AspNetUsers_UserId";
                """);

            migrationBuilder.AlterColumn<string>(
                name: "LastModifiedBy",
                table: "TodoLists",
                type: "text",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CreatedBy",
                table: "TodoLists",
                type: "text",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "LastModifiedBy",
                table: "TodoItems",
                type: "text",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CreatedBy",
                table: "TodoItems",
                type: "text",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "AspNetUserTokens",
                type: "text",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "AspNetUsers",
                type: "text",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "RoleId",
                table: "AspNetUserRoles",
                type: "text",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "AspNetUserRoles",
                type: "text",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "AspNetUserLogins",
                type: "text",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "AspNetUserClaims",
                type: "text",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "AspNetRoles",
                type: "text",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "RoleId",
                table: "AspNetRoleClaims",
                type: "text",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.Sql("""
                ALTER TABLE "AspNetRoleClaims" ADD CONSTRAINT "FK_AspNetRoleClaims_AspNetRoles_RoleId" FOREIGN KEY ("RoleId") REFERENCES "AspNetRoles" ("Id") ON DELETE CASCADE;
                ALTER TABLE "AspNetUserClaims" ADD CONSTRAINT "FK_AspNetUserClaims_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE;
                ALTER TABLE "AspNetUserLogins" ADD CONSTRAINT "FK_AspNetUserLogins_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE;
                ALTER TABLE "AspNetUserRoles" ADD CONSTRAINT "FK_AspNetUserRoles_AspNetRoles_RoleId" FOREIGN KEY ("RoleId") REFERENCES "AspNetRoles" ("Id") ON DELETE CASCADE;
                ALTER TABLE "AspNetUserRoles" ADD CONSTRAINT "FK_AspNetUserRoles_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE;
                ALTER TABLE "AspNetUserTokens" ADD CONSTRAINT "FK_AspNetUserTokens_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE;
                """);
        }
    }
}
