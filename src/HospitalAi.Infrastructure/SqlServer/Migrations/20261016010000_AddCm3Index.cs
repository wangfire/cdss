using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HospitalAi.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddCm3Index : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cm3_index",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    seq_no = table.Column<int>(type: "int", nullable: false),
                    raw_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    raw_name_en = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    index_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    index_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    index_level = table.Column<int>(type: "int", nullable: false),
                    parent_seq_no = table.Column<int>(type: "int", nullable: false),
                    full_index_name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    lead_word_status = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cm3_index", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cm3_index_code",
                table: "cm3_index",
                column: "index_code");

            migrationBuilder.CreateIndex(
                name: "ix_cm3_index_lead_word_status",
                table: "cm3_index",
                column: "lead_word_status");

            migrationBuilder.CreateIndex(
                name: "ix_cm3_index_parent_seq_no",
                table: "cm3_index",
                column: "parent_seq_no");

            migrationBuilder.CreateIndex(
                name: "ux_cm3_index_seq_no",
                table: "cm3_index",
                column: "seq_no",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cm3_index");
        }
    }
}
