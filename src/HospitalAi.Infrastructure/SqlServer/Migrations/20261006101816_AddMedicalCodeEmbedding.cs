using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HospitalAi.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddMedicalCodeEmbedding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "medical_code_embedding",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    hospital_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code_system_code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    model_code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    dim = table.Column<int>(type: "int", nullable: false),
                    vector = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medical_code_embedding", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_medical_code_embedding_hospital_model",
                table: "medical_code_embedding",
                columns: new[] { "hospital_id", "model_code" });

            migrationBuilder.CreateIndex(
                name: "ux_medical_code_embedding_lookup",
                table: "medical_code_embedding",
                columns: new[] { "hospital_id", "model_code", "code_system_code", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "medical_code_embedding");
        }
    }
}
