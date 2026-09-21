using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HospitalAi.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class V22LiteTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_pipeline_trace_step_trace_step",
                table: "pipeline_trace_step");

            migrationBuilder.DropIndex(
                name: "IX_document_section_medical_document_id",
                table: "document_section");

            migrationBuilder.DropIndex(
                name: "ux_coding_rule_hospital_rule",
                table: "coding_rule");

            migrationBuilder.DropIndex(
                name: "IX_coding_recommendation_hospital_id",
                table: "coding_recommendation");

            migrationBuilder.DropIndex(
                name: "ux_coding_recommendation_task_type_code",
                table: "coding_recommendation");

            migrationBuilder.AddColumn<string>(
                name: "evidence_level",
                table: "recommendation_evidence",
                type: "nvarchar(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "pipeline_run_id",
                table: "recommendation_evidence",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "diagnosis_input_id",
                table: "pipeline_trace_step",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "duration_ms",
                table: "pipeline_trace_step",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "input_tokens",
                table: "pipeline_trace_step",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "knowledge_version",
                table: "pipeline_trace_step",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "model_version",
                table: "pipeline_trace_step",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "output_tokens",
                table: "pipeline_trace_step",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "pipeline_run_id",
                table: "pipeline_trace_step",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "prompt_version",
                table: "pipeline_trace_step",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rule_version",
                table: "pipeline_trace_step",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "stage",
                table: "pipeline_trace_step",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "document_status",
                table: "medical_document",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "is_current",
                table: "medical_document",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ocr_version",
                table: "medical_document",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "parse_version",
                table: "medical_document",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "source_document_id",
                table: "medical_document",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "source_updated_at",
                table: "medical_document",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "content_hash",
                table: "document_section",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "embedding_status",
                table: "document_section",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "end_position",
                table: "document_section",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "index_status",
                table: "document_section",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "start_position",
                table: "document_section",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "token_count",
                table: "document_section",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "coding_stage",
                table: "coding_task",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "action_json",
                table: "coding_rule",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "blocking",
                table: "coding_rule",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "condition_json",
                table: "coding_rule",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_builtin",
                table: "coding_rule",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "priority",
                table: "coding_rule",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "rule_group",
                table: "coding_rule",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rule_version",
                table: "coding_rule",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "coding_version",
                table: "coding_recommendation",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "diagnosis_input_id",
                table: "coding_recommendation",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "evidence_sufficiency",
                table: "coding_recommendation",
                type: "decimal(5,4)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_read_only",
                table: "coding_recommendation",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "knowledge_version",
                table: "coding_recommendation",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "lifecycle_status",
                table: "coding_recommendation",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "model_version",
                table: "coding_recommendation",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "outcome",
                table: "coding_recommendation",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "pipeline_run_id",
                table: "coding_recommendation",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pipeline_version",
                table: "coding_recommendation",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "prompt_version",
                table: "coding_recommendation",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reason",
                table: "coding_recommendation",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recommendation_version",
                table: "coding_recommendation",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "risk_level",
                table: "coding_recommendation",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rule_version",
                table: "coding_recommendation",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "clinical_evidence",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    hospital_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    visit_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    coding_task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    pipeline_run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    evidence_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    source_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    section_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    original_text = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    start_position = table.Column<int>(type: "int", nullable: false),
                    end_position = table.Column<int>(type: "int", nullable: false),
                    evidence_level = table.Column<int>(type: "int", nullable: false),
                    source_reliability = table.Column<decimal>(type: "decimal(5,4)", nullable: false),
                    temporal_validity = table.Column<decimal>(type: "decimal(5,4)", nullable: false),
                    text_completeness = table.Column<decimal>(type: "decimal(5,4)", nullable: false),
                    evidence_score = table.Column<decimal>(type: "decimal(5,4)", nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clinical_evidence", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "clinical_fact",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    hospital_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    visit_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    coding_task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    pipeline_run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    fact_type = table.Column<int>(type: "int", nullable: false),
                    fact_name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    normalized_value = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    original_value = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    negation = table.Column<bool>(type: "bit", nullable: false),
                    certainty = table.Column<int>(type: "int", nullable: false),
                    temporality = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    confidence = table.Column<decimal>(type: "decimal(5,4)", nullable: false),
                    source_document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    source_section_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    source_start = table.Column<int>(type: "int", nullable: true),
                    source_end = table.Column<int>(type: "int", nullable: true),
                    extractor_version = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clinical_fact", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "clinical_fact_evidence",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    hospital_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    fact_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    evidence_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clinical_fact_evidence", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "coding_candidate",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    hospital_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    coding_task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    diagnosis_input_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    pipeline_run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code_system = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    recall_source = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    exact_score = table.Column<decimal>(type: "decimal(5,4)", nullable: true),
                    bm25_score = table.Column<decimal>(type: "decimal(5,4)", nullable: true),
                    vector_score = table.Column<decimal>(type: "decimal(5,4)", nullable: true),
                    rerank_score = table.Column<decimal>(type: "decimal(5,4)", nullable: true),
                    rule_score = table.Column<decimal>(type: "decimal(5,4)", nullable: true),
                    evidence_score = table.Column<decimal>(type: "decimal(5,4)", nullable: true),
                    final_score = table.Column<decimal>(type: "decimal(5,4)", nullable: false),
                    rank = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coding_candidate", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "coding_diagnosis_input",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    hospital_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    visit_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    coding_task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    source_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    original_text = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    normalized_text = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    is_principal = table.Column<bool>(type: "bit", nullable: false),
                    diagnosis_order = table.Column<int>(type: "int", nullable: false),
                    source_document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    source_section_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    source_start = table.Column<int>(type: "int", nullable: true),
                    source_end = table.Column<int>(type: "int", nullable: true),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coding_diagnosis_input", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "quality_issue",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    hospital_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    visit_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    coding_task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    diagnosis_input_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    issue_type = table.Column<int>(type: "int", nullable: false),
                    risk_level = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    fact_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    evidence_ids = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    current_code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    suggested_code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quality_issue", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "recommendation_score",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    hospital_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    recommendation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    exact_score = table.Column<decimal>(type: "decimal(5,4)", nullable: true),
                    semantic_score = table.Column<decimal>(type: "decimal(5,4)", nullable: true),
                    retrieval_score = table.Column<decimal>(type: "decimal(5,4)", nullable: true),
                    rerank_score = table.Column<decimal>(type: "decimal(5,4)", nullable: true),
                    rule_score = table.Column<decimal>(type: "decimal(5,4)", nullable: true),
                    evidence_score = table.Column<decimal>(type: "decimal(5,4)", nullable: true),
                    llm_score = table.Column<decimal>(type: "decimal(5,4)", nullable: true),
                    margin_score = table.Column<decimal>(type: "decimal(5,4)", nullable: true),
                    score_profile = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recommendation_score", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pipeline_trace_step_run_stage",
                table: "pipeline_trace_step",
                columns: new[] { "pipeline_run_id", "stage" });

            migrationBuilder.CreateIndex(
                name: "ix_pipeline_trace_step_trace_started",
                table: "pipeline_trace_step",
                columns: new[] { "pipeline_trace_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_medical_document_visit_type_current",
                table: "medical_document",
                columns: new[] { "hospital_id", "visit_id", "document_type", "is_current" });

            migrationBuilder.CreateIndex(
                name: "ix_document_section_document_hash",
                table: "document_section",
                columns: new[] { "medical_document_id", "content_hash" });

            migrationBuilder.CreateIndex(
                name: "ux_coding_rule_hospital_rule_version",
                table: "coding_rule",
                columns: new[] { "hospital_id", "rule_code", "rule_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_coding_recommendation_hospital_pipeline_lifecycle",
                table: "coding_recommendation",
                columns: new[] { "hospital_id", "pipeline_version", "lifecycle_status" });

            migrationBuilder.CreateIndex(
                name: "ux_coding_recommendation_task_input_type_code",
                table: "coding_recommendation",
                columns: new[] { "coding_task_id", "diagnosis_input_id", "recommendation_type", "code" },
                unique: true,
                filter: "[diagnosis_input_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_clinical_evidence_hospital_visit",
                table: "clinical_evidence",
                columns: new[] { "hospital_id", "visit_id" });

            migrationBuilder.CreateIndex(
                name: "ix_clinical_evidence_task_run",
                table: "clinical_evidence",
                columns: new[] { "coding_task_id", "pipeline_run_id" });

            migrationBuilder.CreateIndex(
                name: "ix_clinical_fact_hospital_visit",
                table: "clinical_fact",
                columns: new[] { "hospital_id", "visit_id" });

            migrationBuilder.CreateIndex(
                name: "ix_clinical_fact_task_run",
                table: "clinical_fact",
                columns: new[] { "coding_task_id", "pipeline_run_id" });

            migrationBuilder.CreateIndex(
                name: "ux_clinical_fact_evidence_fact_evidence",
                table: "clinical_fact_evidence",
                columns: new[] { "hospital_id", "fact_id", "evidence_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_coding_candidate_input_run",
                table: "coding_candidate",
                columns: new[] { "hospital_id", "diagnosis_input_id", "pipeline_run_id" });

            migrationBuilder.CreateIndex(
                name: "ix_coding_diagnosis_input_task_order",
                table: "coding_diagnosis_input",
                columns: new[] { "hospital_id", "coding_task_id", "diagnosis_order" });

            migrationBuilder.CreateIndex(
                name: "ix_quality_issue_task_status",
                table: "quality_issue",
                columns: new[] { "hospital_id", "coding_task_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_recommendation_score_recommendation",
                table: "recommendation_score",
                column: "recommendation_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "clinical_evidence");

            migrationBuilder.DropTable(
                name: "clinical_fact");

            migrationBuilder.DropTable(
                name: "clinical_fact_evidence");

            migrationBuilder.DropTable(
                name: "coding_candidate");

            migrationBuilder.DropTable(
                name: "coding_diagnosis_input");

            migrationBuilder.DropTable(
                name: "quality_issue");

            migrationBuilder.DropTable(
                name: "recommendation_score");

            migrationBuilder.DropIndex(
                name: "ix_pipeline_trace_step_run_stage",
                table: "pipeline_trace_step");

            migrationBuilder.DropIndex(
                name: "ix_pipeline_trace_step_trace_started",
                table: "pipeline_trace_step");

            migrationBuilder.DropIndex(
                name: "ix_medical_document_visit_type_current",
                table: "medical_document");

            migrationBuilder.DropIndex(
                name: "ix_document_section_document_hash",
                table: "document_section");

            migrationBuilder.DropIndex(
                name: "ux_coding_rule_hospital_rule_version",
                table: "coding_rule");

            migrationBuilder.DropIndex(
                name: "ix_coding_recommendation_hospital_pipeline_lifecycle",
                table: "coding_recommendation");

            migrationBuilder.DropIndex(
                name: "ux_coding_recommendation_task_input_type_code",
                table: "coding_recommendation");

            migrationBuilder.DropColumn(
                name: "evidence_level",
                table: "recommendation_evidence");

            migrationBuilder.DropColumn(
                name: "pipeline_run_id",
                table: "recommendation_evidence");

            migrationBuilder.DropColumn(
                name: "diagnosis_input_id",
                table: "pipeline_trace_step");

            migrationBuilder.DropColumn(
                name: "duration_ms",
                table: "pipeline_trace_step");

            migrationBuilder.DropColumn(
                name: "input_tokens",
                table: "pipeline_trace_step");

            migrationBuilder.DropColumn(
                name: "knowledge_version",
                table: "pipeline_trace_step");

            migrationBuilder.DropColumn(
                name: "model_version",
                table: "pipeline_trace_step");

            migrationBuilder.DropColumn(
                name: "output_tokens",
                table: "pipeline_trace_step");

            migrationBuilder.DropColumn(
                name: "pipeline_run_id",
                table: "pipeline_trace_step");

            migrationBuilder.DropColumn(
                name: "prompt_version",
                table: "pipeline_trace_step");

            migrationBuilder.DropColumn(
                name: "rule_version",
                table: "pipeline_trace_step");

            migrationBuilder.DropColumn(
                name: "stage",
                table: "pipeline_trace_step");

            migrationBuilder.DropColumn(
                name: "document_status",
                table: "medical_document");

            migrationBuilder.DropColumn(
                name: "is_current",
                table: "medical_document");

            migrationBuilder.DropColumn(
                name: "ocr_version",
                table: "medical_document");

            migrationBuilder.DropColumn(
                name: "parse_version",
                table: "medical_document");

            migrationBuilder.DropColumn(
                name: "source_document_id",
                table: "medical_document");

            migrationBuilder.DropColumn(
                name: "source_updated_at",
                table: "medical_document");

            migrationBuilder.DropColumn(
                name: "content_hash",
                table: "document_section");

            migrationBuilder.DropColumn(
                name: "embedding_status",
                table: "document_section");

            migrationBuilder.DropColumn(
                name: "end_position",
                table: "document_section");

            migrationBuilder.DropColumn(
                name: "index_status",
                table: "document_section");

            migrationBuilder.DropColumn(
                name: "start_position",
                table: "document_section");

            migrationBuilder.DropColumn(
                name: "token_count",
                table: "document_section");

            migrationBuilder.DropColumn(
                name: "coding_stage",
                table: "coding_task");

            migrationBuilder.DropColumn(
                name: "action_json",
                table: "coding_rule");

            migrationBuilder.DropColumn(
                name: "blocking",
                table: "coding_rule");

            migrationBuilder.DropColumn(
                name: "condition_json",
                table: "coding_rule");

            migrationBuilder.DropColumn(
                name: "is_builtin",
                table: "coding_rule");

            migrationBuilder.DropColumn(
                name: "priority",
                table: "coding_rule");

            migrationBuilder.DropColumn(
                name: "rule_group",
                table: "coding_rule");

            migrationBuilder.DropColumn(
                name: "rule_version",
                table: "coding_rule");

            migrationBuilder.DropColumn(
                name: "coding_version",
                table: "coding_recommendation");

            migrationBuilder.DropColumn(
                name: "diagnosis_input_id",
                table: "coding_recommendation");

            migrationBuilder.DropColumn(
                name: "evidence_sufficiency",
                table: "coding_recommendation");

            migrationBuilder.DropColumn(
                name: "is_read_only",
                table: "coding_recommendation");

            migrationBuilder.DropColumn(
                name: "knowledge_version",
                table: "coding_recommendation");

            migrationBuilder.DropColumn(
                name: "lifecycle_status",
                table: "coding_recommendation");

            migrationBuilder.DropColumn(
                name: "model_version",
                table: "coding_recommendation");

            migrationBuilder.DropColumn(
                name: "outcome",
                table: "coding_recommendation");

            migrationBuilder.DropColumn(
                name: "pipeline_run_id",
                table: "coding_recommendation");

            migrationBuilder.DropColumn(
                name: "pipeline_version",
                table: "coding_recommendation");

            migrationBuilder.DropColumn(
                name: "prompt_version",
                table: "coding_recommendation");

            migrationBuilder.DropColumn(
                name: "reason",
                table: "coding_recommendation");

            migrationBuilder.DropColumn(
                name: "recommendation_version",
                table: "coding_recommendation");

            migrationBuilder.DropColumn(
                name: "risk_level",
                table: "coding_recommendation");

            migrationBuilder.DropColumn(
                name: "rule_version",
                table: "coding_recommendation");

            migrationBuilder.CreateIndex(
                name: "ux_pipeline_trace_step_trace_step",
                table: "pipeline_trace_step",
                columns: new[] { "pipeline_trace_id", "step_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_section_medical_document_id",
                table: "document_section",
                column: "medical_document_id");

            migrationBuilder.CreateIndex(
                name: "ux_coding_rule_hospital_rule",
                table: "coding_rule",
                columns: new[] { "hospital_id", "rule_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_coding_recommendation_hospital_id",
                table: "coding_recommendation",
                column: "hospital_id");

            migrationBuilder.CreateIndex(
                name: "ux_coding_recommendation_task_type_code",
                table: "coding_recommendation",
                columns: new[] { "coding_task_id", "recommendation_type", "code" },
                unique: true);
        }
    }
}
