using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanArchitecture.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PersonalDataErasureRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PurgePolicyId",
                table: "IdentityDocuments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PurgePolicyVersion",
                table: "IdentityDocuments",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PersonalDataErasureRecords",
                columns: table => new
                {
                    RecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectIdentityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PolicyId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PolicyVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExecutedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AffectedRowCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonalDataErasureRecords", x => x.RecordId);
                    table.CheckConstraint("CK_PersonalDataErasureRecords_Evidence", "\"AffectedRowCount\" > 0 AND length(\"PolicyId\") > 0 AND length(\"PolicyVersion\") > 0 AND length(\"Category\") > 0");
                    table.CheckConstraint("CK_PersonalDataErasureRecords_Ids_NotEmpty", "\"RecordId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"SubjectIdentityId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_PersonalDataErasureRecords_AspNetUsers_SubjectIdentityId",
                        column: x => x.SubjectIdentityId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PersonalDataErasureRecords_SubjectIdentityId_Category",
                table: "PersonalDataErasureRecords",
                columns: new[] { "SubjectIdentityId", "Category" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PersonalDataErasureRecords");

            migrationBuilder.DropColumn(
                name: "PurgePolicyId",
                table: "IdentityDocuments");

            migrationBuilder.DropColumn(
                name: "PurgePolicyVersion",
                table: "IdentityDocuments");
        }
    }
}
