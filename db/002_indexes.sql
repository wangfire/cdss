-- Phase 1 索引脚本。
-- 所有索引创建前都会检查名称，脚本可重复执行。
SET XACT_ABORT ON;

BEGIN TRANSACTION;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'ux_hospital_code'
      AND object_id = OBJECT_ID(N'dbo.hospital')
)
BEGIN
    CREATE UNIQUE INDEX ux_hospital_code
        ON dbo.hospital (code);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'ux_patient_hospital_source'
      AND object_id = OBJECT_ID(N'dbo.patient')
)
BEGIN
    CREATE UNIQUE INDEX ux_patient_hospital_source
        ON dbo.patient (hospital_id, source_system, source_patient_id);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'ux_coding_task_hospital_visit_pipeline'
      AND object_id = OBJECT_ID(N'dbo.coding_task')
)
BEGIN
    CREATE UNIQUE INDEX ux_coding_task_hospital_visit_pipeline
        ON dbo.coding_task (hospital_id, visit_id, pipeline_version);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'ux_coding_task_hospital_idempotency_key'
      AND object_id = OBJECT_ID(N'dbo.coding_task')
)
BEGIN
    CREATE UNIQUE INDEX ux_coding_task_hospital_idempotency_key
        ON dbo.coding_task (hospital_id, idempotency_key)
        WHERE idempotency_key IS NOT NULL;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'ux_medical_document_visit_type_version'
      AND object_id = OBJECT_ID(N'dbo.medical_document')
)
BEGIN
    CREATE UNIQUE INDEX ux_medical_document_visit_type_version
        ON dbo.medical_document (hospital_id, visit_id, document_type, version);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'ix_medical_document_visit_id'
      AND object_id = OBJECT_ID(N'dbo.medical_document')
)
BEGIN
    CREATE INDEX ix_medical_document_visit_id
        ON dbo.medical_document (visit_id);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'ix_outbox_message_pending'
      AND object_id = OBJECT_ID(N'dbo.outbox_message')
)
BEGIN
    CREATE INDEX ix_outbox_message_pending
        ON dbo.outbox_message (published_at, occurred_at);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'ux_inbox_message_message_consumer'
      AND object_id = OBJECT_ID(N'dbo.inbox_message')
)
BEGIN
    CREATE UNIQUE INDEX ux_inbox_message_message_consumer
        ON dbo.inbox_message (message_id, consumer_name);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'ux_pipeline_trace_trace_id'
      AND object_id = OBJECT_ID(N'dbo.pipeline_trace')
)
BEGIN
    CREATE UNIQUE INDEX ux_pipeline_trace_trace_id
        ON dbo.pipeline_trace (trace_id);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'ux_pipeline_trace_step_trace_step'
      AND object_id = OBJECT_ID(N'dbo.pipeline_trace_step')
)
BEGIN
    CREATE UNIQUE INDEX ux_pipeline_trace_step_trace_step
        ON dbo.pipeline_trace_step (pipeline_trace_id, step_name);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'ix_audit_log_hospital_created'
      AND object_id = OBJECT_ID(N'dbo.audit_log')
)
BEGIN
    CREATE INDEX ix_audit_log_hospital_created
        ON dbo.audit_log (hospital_id, created_at);
END;

COMMIT TRANSACTION;
