using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HospitalAi.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Phase2CodingMvp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "code_system",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    hospital_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    version = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_code_system", x => x.id);
                    table.ForeignKey(
                        name: "FK_code_system_hospital_hospital_id",
                        column: x => x.hospital_id,
                        principalTable: "hospital",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "coding_recommendation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    hospital_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    coding_task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    recommendation_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    code_system_code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    rank = table.Column<int>(type: "int", nullable: false),
                    recall_score = table.Column<decimal>(type: "decimal(5,4)", nullable: false),
                    rule_score = table.Column<decimal>(type: "decimal(5,4)", nullable: false),
                    confidence_score = table.Column<decimal>(type: "decimal(5,4)", nullable: false),
                    review_status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coding_recommendation", x => x.id);
                    table.ForeignKey(
                        name: "FK_coding_recommendation_coding_task_coding_task_id",
                        column: x => x.coding_task_id,
                        principalTable: "coding_task",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_coding_recommendation_hospital_hospital_id",
                        column: x => x.hospital_id,
                        principalTable: "hospital",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "coding_review",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    hospital_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    coding_task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    review_status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    reviewer_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coding_review", x => x.id);
                    table.ForeignKey(
                        name: "FK_coding_review_coding_task_coding_task_id",
                        column: x => x.coding_task_id,
                        principalTable: "coding_task",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_coding_review_hospital_hospital_id",
                        column: x => x.hospital_id,
                        principalTable: "hospital",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "coding_rule",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    hospital_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    rule_code = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    code_system_code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    code_pattern = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    rule_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    severity = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    is_enabled = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coding_rule", x => x.id);
                    table.ForeignKey(
                        name: "FK_coding_rule_hospital_hospital_id",
                        column: x => x.hospital_id,
                        principalTable: "hospital",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_section",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    hospital_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    visit_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    medical_document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    section_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    sequence = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_section", x => x.id);
                    table.ForeignKey(
                        name: "FK_document_section_hospital_hospital_id",
                        column: x => x.hospital_id,
                        principalTable: "hospital",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_document_section_medical_document_medical_document_id",
                        column: x => x.medical_document_id,
                        principalTable: "medical_document",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_document_section_visit_visit_id",
                        column: x => x.visit_id,
                        principalTable: "visit",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "term_synonym",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    hospital_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    term = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    normalized_term = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    entity_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_term_synonym", x => x.id);
                    table.ForeignKey(
                        name: "FK_term_synonym_hospital_hospital_id",
                        column: x => x.hospital_id,
                        principalTable: "hospital",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "medical_code",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    hospital_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code_system_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code_system_code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    code_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    search_text = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    is_enabled = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medical_code", x => x.id);
                    table.ForeignKey(
                        name: "FK_medical_code_code_system_code_system_id",
                        column: x => x.code_system_id,
                        principalTable: "code_system",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_medical_code_hospital_hospital_id",
                        column: x => x.hospital_id,
                        principalTable: "hospital",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "final_coding_result",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    hospital_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    coding_task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    source_recommendation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    result_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    code_system_code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    reviewer_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_final_coding_result", x => x.id);
                    table.ForeignKey(
                        name: "FK_final_coding_result_coding_recommendation_source_recommendation_id",
                        column: x => x.source_recommendation_id,
                        principalTable: "coding_recommendation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_final_coding_result_coding_task_coding_task_id",
                        column: x => x.coding_task_id,
                        principalTable: "coding_task",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_final_coding_result_hospital_hospital_id",
                        column: x => x.hospital_id,
                        principalTable: "hospital",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "clinical_entity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    hospital_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    coding_task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    document_section_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    entity_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    raw_text = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    normalized_text = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    is_negated = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clinical_entity", x => x.id);
                    table.ForeignKey(
                        name: "FK_clinical_entity_coding_task_coding_task_id",
                        column: x => x.coding_task_id,
                        principalTable: "coding_task",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_clinical_entity_document_section_document_section_id",
                        column: x => x.document_section_id,
                        principalTable: "document_section",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_clinical_entity_hospital_hospital_id",
                        column: x => x.hospital_id,
                        principalTable: "hospital",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recommendation_evidence",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    hospital_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    coding_recommendation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    document_section_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    source_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    source_text = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    match_text = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    score = table.Column<decimal>(type: "decimal(5,4)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recommendation_evidence", x => x.id);
                    table.ForeignKey(
                        name: "FK_recommendation_evidence_coding_recommendation_coding_recommendation_id",
                        column: x => x.coding_recommendation_id,
                        principalTable: "coding_recommendation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_recommendation_evidence_document_section_document_section_id",
                        column: x => x.document_section_id,
                        principalTable: "document_section",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_recommendation_evidence_hospital_hospital_id",
                        column: x => x.hospital_id,
                        principalTable: "hospital",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_clinical_entity_coding_task_id",
                table: "clinical_entity",
                column: "coding_task_id");

            migrationBuilder.CreateIndex(
                name: "IX_clinical_entity_document_section_id",
                table: "clinical_entity",
                column: "document_section_id");

            migrationBuilder.CreateIndex(
                name: "ix_clinical_entity_task_type",
                table: "clinical_entity",
                columns: new[] { "hospital_id", "coding_task_id", "entity_type" });

            migrationBuilder.CreateIndex(
                name: "ux_code_system_hospital_code",
                table: "code_system",
                columns: new[] { "hospital_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_coding_recommendation_hospital_id",
                table: "coding_recommendation",
                column: "hospital_id");

            migrationBuilder.CreateIndex(
                name: "ix_coding_recommendation_task_review_status",
                table: "coding_recommendation",
                columns: new[] { "coding_task_id", "review_status" });

            migrationBuilder.CreateIndex(
                name: "ux_coding_recommendation_task_type_code",
                table: "coding_recommendation",
                columns: new[] { "coding_task_id", "recommendation_type", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_coding_review_hospital_id",
                table: "coding_review",
                column: "hospital_id");

            migrationBuilder.CreateIndex(
                name: "ix_coding_review_task_status",
                table: "coding_review",
                columns: new[] { "coding_task_id", "review_status" });

            migrationBuilder.CreateIndex(
                name: "ux_coding_rule_hospital_rule",
                table: "coding_rule",
                columns: new[] { "hospital_id", "rule_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_section_medical_document_id",
                table: "document_section",
                column: "medical_document_id");

            migrationBuilder.CreateIndex(
                name: "IX_document_section_visit_id",
                table: "document_section",
                column: "visit_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_section_visit_sequence",
                table: "document_section",
                columns: new[] { "hospital_id", "visit_id", "sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_final_coding_result_hospital_id",
                table: "final_coding_result",
                column: "hospital_id");

            migrationBuilder.CreateIndex(
                name: "IX_final_coding_result_source_recommendation_id",
                table: "final_coding_result",
                column: "source_recommendation_id");

            migrationBuilder.CreateIndex(
                name: "ux_final_coding_result_task_type_code",
                table: "final_coding_result",
                columns: new[] { "coding_task_id", "result_type", "code_system_code", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_medical_code_code_system_id",
                table: "medical_code",
                column: "code_system_id");

            migrationBuilder.CreateIndex(
                name: "ix_medical_code_hospital_system_type",
                table: "medical_code",
                columns: new[] { "hospital_id", "code_system_code", "code_type" });

            migrationBuilder.CreateIndex(
                name: "ux_medical_code_hospital_system_code",
                table: "medical_code",
                columns: new[] { "hospital_id", "code_system_code", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_recommendation_evidence_document_section_id",
                table: "recommendation_evidence",
                column: "document_section_id");

            migrationBuilder.CreateIndex(
                name: "IX_recommendation_evidence_hospital_id",
                table: "recommendation_evidence",
                column: "hospital_id");

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_evidence_recommendation",
                table: "recommendation_evidence",
                column: "coding_recommendation_id");

            migrationBuilder.CreateIndex(
                name: "ux_term_synonym_hospital_term_type",
                table: "term_synonym",
                columns: new[] { "hospital_id", "term", "entity_type" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "clinical_entity");

            migrationBuilder.DropTable(
                name: "coding_review");

            migrationBuilder.DropTable(
                name: "coding_rule");

            migrationBuilder.DropTable(
                name: "final_coding_result");

            migrationBuilder.DropTable(
                name: "medical_code");

            migrationBuilder.DropTable(
                name: "recommendation_evidence");

            migrationBuilder.DropTable(
                name: "term_synonym");

            migrationBuilder.DropTable(
                name: "code_system");

            migrationBuilder.DropTable(
                name: "coding_recommendation");

            migrationBuilder.DropTable(
                name: "document_section");
        }
    }
}
