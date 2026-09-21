using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HospitalAi.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class V22LiteRecommendationVersionIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_coding_recommendation_task_input_type_code",
                table: "coding_recommendation");

            migrationBuilder.CreateIndex(
                name: "ux_coding_recommendation_task_input_type_code",
                table: "coding_recommendation",
                columns: new[] { "coding_task_id", "diagnosis_input_id", "recommendation_type", "code" },
                unique: true,
                filter: "[recommendation_version] IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_coding_recommendation_task_input_type_code_version",
                table: "coding_recommendation",
                columns: new[] { "coding_task_id", "diagnosis_input_id", "recommendation_type", "code", "recommendation_version" },
                unique: true,
                filter: "[recommendation_version] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_coding_recommendation_task_input_type_code",
                table: "coding_recommendation");

            migrationBuilder.DropIndex(
                name: "ux_coding_recommendation_task_input_type_code_version",
                table: "coding_recommendation");

            migrationBuilder.CreateIndex(
                name: "ux_coding_recommendation_task_input_type_code",
                table: "coding_recommendation",
                columns: new[] { "coding_task_id", "diagnosis_input_id", "recommendation_type", "code" },
                unique: true,
                filter: "[diagnosis_input_id] IS NOT NULL");
        }
    }
}
