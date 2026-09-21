using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HospitalAi.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class TermSynonymCodeMapping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_term_synonym_hospital_term_type",
                table: "term_synonym");

            migrationBuilder.AddColumn<string>(
                name: "code",
                table: "term_synonym",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "code_system_code",
                table: "term_synonym",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ux_term_synonym_hospital_term_type_code",
                table: "term_synonym",
                columns: new[] { "hospital_id", "term", "entity_type", "code_system_code", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_term_synonym_hospital_term_type_code",
                table: "term_synonym");

            migrationBuilder.DropColumn(
                name: "code",
                table: "term_synonym");

            migrationBuilder.DropColumn(
                name: "code_system_code",
                table: "term_synonym");

            migrationBuilder.CreateIndex(
                name: "ux_term_synonym_hospital_term_type",
                table: "term_synonym",
                columns: new[] { "hospital_id", "term", "entity_type" },
                unique: true);
        }
    }
}
