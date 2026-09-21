using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HospitalAi.Infrastructure.SqlServer.Migrations
{
    /// <summary>
    /// V2.2-Lite：coding_review 增加审核动作类型。
    /// 新增列一律 nullable，不删除旧列，后续再逐步收紧。
    /// </summary>
    public partial class V22LiteReviewResultType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "result_type",
                table: "coding_review",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "result_type",
                table: "coding_review");
        }
    }
}
