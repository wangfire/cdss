using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HospitalAi.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddIcdTermSynonym : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "icd_term_synonym",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    term_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    canonical_term = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    variant_term = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    is_enabled = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_icd_term_synonym", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_icd_term_synonym_canonical",
                table: "icd_term_synonym",
                column: "canonical_term");

            migrationBuilder.CreateIndex(
                name: "ux_icd_term_synonym_type_variant",
                table: "icd_term_synonym",
                columns: new[] { "term_type", "variant_term" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "icd_term_synonym");
        }
    }
}
