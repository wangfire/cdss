using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HospitalAi.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Task6Application : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                table: "coding_task",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "medical_document",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    hospital_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    visit_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    document_type = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    content_reference = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    content_hash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medical_document", x => x.id);
                    table.ForeignKey(
                        name: "FK_medical_document_hospital_hospital_id",
                        column: x => x.hospital_id,
                        principalTable: "hospital",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_medical_document_visit_visit_id",
                        column: x => x.visit_id,
                        principalTable: "visit",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_coding_task_hospital_idempotency_key",
                table: "coding_task",
                columns: new[] { "hospital_id", "idempotency_key" },
                unique: true,
                filter: "[idempotency_key] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_medical_document_visit_id",
                table: "medical_document",
                column: "visit_id");

            migrationBuilder.CreateIndex(
                name: "ux_medical_document_visit_type_version",
                table: "medical_document",
                columns: new[] { "hospital_id", "visit_id", "document_type", "version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "medical_document");

            migrationBuilder.DropIndex(
                name: "ux_coding_task_hospital_idempotency_key",
                table: "coding_task");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                table: "coding_task");
        }
    }
}
