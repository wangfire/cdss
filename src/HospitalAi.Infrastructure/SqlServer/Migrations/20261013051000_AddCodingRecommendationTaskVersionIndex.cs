using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HospitalAi.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddCodingRecommendationTaskVersionIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_coding_recommendation_task_input_version",
                table: "coding_recommendation",
                columns: new[] { "coding_task_id", "hospital_id", "diagnosis_input_id", "recommendation_version" },
                filter: "[diagnosis_input_id] IS NOT NULL AND [recommendation_version] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_coding_recommendation_task_input_version",
                table: "coding_recommendation");
        }
    }
}
