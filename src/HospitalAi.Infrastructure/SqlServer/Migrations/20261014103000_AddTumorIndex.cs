using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HospitalAi.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddTumorIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tumor_index",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    seq_no = table.Column<int>(type: "int", nullable: false),
                    index_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    index_name_en = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    index_level = table.Column<int>(type: "int", nullable: false),
                    parent_seq_no = table.Column<int>(type: "int", nullable: false),
                    full_index_name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    code_malignant_primary = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    code_malignant_secondary = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    code_in_situ = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    code_benign = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    code_uncertain = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    jump_kind = table.Column<int>(type: "int", nullable: false),
                    jump_target = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tumor_index", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tumor_index_parent_seq_no",
                table: "tumor_index",
                column: "parent_seq_no");

            migrationBuilder.CreateIndex(
                name: "ux_tumor_index_seq_no",
                table: "tumor_index",
                column: "seq_no",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tumor_index");
        }
    }
}
