using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HospitalAi.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hospital",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hospital", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    hospital_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    resource_type = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    resource_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    action = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    result = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    request_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    actor_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_log", x => x.id);
                    table.ForeignKey(
                        name: "FK_audit_log_hospital_hospital_id",
                        column: x => x.hospital_id,
                        principalTable: "hospital",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inbox_message",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    hospital_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    message_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    consumer_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox_message", x => x.id);
                    table.ForeignKey(
                        name: "FK_inbox_message_hospital_hospital_id",
                        column: x => x.hospital_id,
                        principalTable: "hospital",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "outbox_message",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    hospital_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    message_type = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    payload_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_message", x => x.id);
                    table.ForeignKey(
                        name: "FK_outbox_message_hospital_hospital_id",
                        column: x => x.hospital_id,
                        principalTable: "hospital",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "patient",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    hospital_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    source_system = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    source_patient_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    display_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_patient", x => x.id);
                    table.ForeignKey(
                        name: "FK_patient_hospital_hospital_id",
                        column: x => x.hospital_id,
                        principalTable: "hospital",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "visit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    hospital_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    patient_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    admission_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    discharge_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_visit", x => x.id);
                    table.ForeignKey(
                        name: "FK_visit_hospital_hospital_id",
                        column: x => x.hospital_id,
                        principalTable: "hospital",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_visit_patient_patient_id",
                        column: x => x.patient_id,
                        principalTable: "patient",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "coding_task",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    hospital_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    visit_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    pipeline_version = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    status = table.Column<int>(type: "int", nullable: false),
                    retry_count = table.Column<int>(type: "int", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    error_code = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coding_task", x => x.id);
                    table.ForeignKey(
                        name: "FK_coding_task_hospital_hospital_id",
                        column: x => x.hospital_id,
                        principalTable: "hospital",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_coding_task_visit_visit_id",
                        column: x => x.visit_id,
                        principalTable: "visit",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pipeline_trace",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    hospital_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    coding_task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    trace_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pipeline_trace", x => x.id);
                    table.ForeignKey(
                        name: "FK_pipeline_trace_coding_task_coding_task_id",
                        column: x => x.coding_task_id,
                        principalTable: "coding_task",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pipeline_trace_hospital_hospital_id",
                        column: x => x.hospital_id,
                        principalTable: "hospital",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pipeline_trace_step",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    hospital_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    pipeline_trace_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    step_name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    error_code = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pipeline_trace_step", x => x.id);
                    table.ForeignKey(
                        name: "FK_pipeline_trace_step_hospital_hospital_id",
                        column: x => x.hospital_id,
                        principalTable: "hospital",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pipeline_trace_step_pipeline_trace_pipeline_trace_id",
                        column: x => x.pipeline_trace_id,
                        principalTable: "pipeline_trace",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_hospital_created",
                table: "audit_log",
                columns: new[] { "hospital_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_coding_task_visit_id",
                table: "coding_task",
                column: "visit_id");

            migrationBuilder.CreateIndex(
                name: "ux_coding_task_hospital_visit_pipeline",
                table: "coding_task",
                columns: new[] { "hospital_id", "visit_id", "pipeline_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_hospital_code",
                table: "hospital",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inbox_message_hospital_id",
                table: "inbox_message",
                column: "hospital_id");

            migrationBuilder.CreateIndex(
                name: "ux_inbox_message_message_consumer",
                table: "inbox_message",
                columns: new[] { "message_id", "consumer_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_message_hospital_id",
                table: "outbox_message",
                column: "hospital_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_pending",
                table: "outbox_message",
                columns: new[] { "published_at", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ux_patient_hospital_source",
                table: "patient",
                columns: new[] { "hospital_id", "source_system", "source_patient_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_trace_coding_task_id",
                table: "pipeline_trace",
                column: "coding_task_id");

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_trace_hospital_id",
                table: "pipeline_trace",
                column: "hospital_id");

            migrationBuilder.CreateIndex(
                name: "ux_pipeline_trace_trace_id",
                table: "pipeline_trace",
                column: "trace_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_trace_step_hospital_id",
                table: "pipeline_trace_step",
                column: "hospital_id");

            migrationBuilder.CreateIndex(
                name: "ux_pipeline_trace_step_trace_step",
                table: "pipeline_trace_step",
                columns: new[] { "pipeline_trace_id", "step_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_visit_hospital_id",
                table: "visit",
                column: "hospital_id");

            migrationBuilder.CreateIndex(
                name: "IX_visit_patient_id",
                table: "visit",
                column: "patient_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_log");

            migrationBuilder.DropTable(
                name: "inbox_message");

            migrationBuilder.DropTable(
                name: "outbox_message");

            migrationBuilder.DropTable(
                name: "pipeline_trace_step");

            migrationBuilder.DropTable(
                name: "pipeline_trace");

            migrationBuilder.DropTable(
                name: "coding_task");

            migrationBuilder.DropTable(
                name: "visit");

            migrationBuilder.DropTable(
                name: "patient");

            migrationBuilder.DropTable(
                name: "hospital");
        }
    }
}
