using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HospitalAi.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// 知识库治理（WP-C）：icd_index / cm3_index / tumor_index 三表加 is_enabled 停用开关
    /// （默认 1=启用）。错行「停用」而非「删除」，可回滚；快照构建与检索消费过滤停用行。
    /// </summary>
    public partial class KnowledgeIndexEnabledFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_enabled",
                table: "icd_index",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_enabled",
                table: "cm3_index",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_enabled",
                table: "tumor_index",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_enabled",
                table: "icd_index");

            migrationBuilder.DropColumn(
                name: "is_enabled",
                table: "cm3_index");

            migrationBuilder.DropColumn(
                name: "is_enabled",
                table: "tumor_index");
        }
    }
}
