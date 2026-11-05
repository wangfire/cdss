# -*- coding: utf-8 -*-
"""手写迁移：pipeline_runtime_config 表 + 单行种子。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

seed_json = '{"IcdTitleDirectEnabled":false,"Cm3TitleDirectEnabled":false}'

header = """using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HospitalAi.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// 流水线运行时配置（工作台设置抽屉）：单行键值表存 V22Lite 覆盖项 JSON。
    /// 种子行 v22lite：默认关闭字典标题直达（Icd/Cm3TitleDirectEnabled=false，
    /// 与 2026-10-31 的索引匹配口径一致），其余开关不覆盖（沿用 appsettings 默认）。
    /// </summary>
    public partial class PipelineRuntimeConfig : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pipeline_runtime_config",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    config_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    config_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pipeline_runtime_config", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_runtime_config_config_key",
                table: "pipeline_runtime_config",
                column: "config_key",
                unique: true);

            migrationBuilder.InsertData(
                table: "pipeline_runtime_config",
                columns: new[] { "id", "config_key", "config_json", "updated_at" },
                values: new object[]
                {
                    new Guid("5A1F7C2E-3B4D-4E5F-8A9B-0C1D2E3F4A5B"),
                    "v22lite",
                    \""""

mid = seed_json

tail = """\",
                    new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero))
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pipeline_runtime_config");
        }
    }
}
"""

mig = header + mid + tail
path = r"src\HospitalAi.Infrastructure\SqlServer\Migrations\20261101090000_PipelineRuntimeConfig.cs"
open(path, "w", encoding="utf-8").write(mig)
print("migration written:", path)
