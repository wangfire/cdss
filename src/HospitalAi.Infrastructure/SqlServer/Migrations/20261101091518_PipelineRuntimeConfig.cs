using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HospitalAi.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class PipelineRuntimeConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pipeline_runtime_config",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    config_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    config_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
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
                    "{\"IcdTitleDirectEnabled\":false,\"Cm3TitleDirectEnabled\":false}",
                    new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero)
                });

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pipeline_runtime_config");
        }
    }
}
