-- Phase 1 数据库结构脚本。
-- 该脚本可重复执行，适用于本地开发环境初始化。
SET XACT_ABORT ON;

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.hospital', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.hospital
    (
        id uniqueidentifier NOT NULL,
        code nvarchar(64) NOT NULL,
        name nvarchar(200) NOT NULL,
        status nvarchar(32) NOT NULL,
        created_at datetimeoffset(7) NOT NULL,
        updated_at datetimeoffset(7) NOT NULL,
        row_version rowversion NOT NULL,
        CONSTRAINT pk_hospital PRIMARY KEY (id)
    );
END;

IF OBJECT_ID(N'dbo.patient', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.patient
    (
        id uniqueidentifier NOT NULL,
        hospital_id uniqueidentifier NOT NULL,
        source_system nvarchar(64) NOT NULL,
        source_patient_id nvarchar(128) NOT NULL,
        display_name nvarchar(200) NULL,
        created_at datetimeoffset(7) NOT NULL,
        updated_at datetimeoffset(7) NOT NULL,
        row_version rowversion NOT NULL,
        CONSTRAINT pk_patient PRIMARY KEY (id),
        CONSTRAINT fk_patient_hospital FOREIGN KEY (hospital_id)
            REFERENCES dbo.hospital (id)
    );
END;

IF OBJECT_ID(N'dbo.visit', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.visit
    (
        id uniqueidentifier NOT NULL,
        hospital_id uniqueidentifier NOT NULL,
        patient_id uniqueidentifier NOT NULL,
        admission_at datetimeoffset(7) NOT NULL,
        discharge_at datetimeoffset(7) NULL,
        created_at datetimeoffset(7) NOT NULL,
        updated_at datetimeoffset(7) NOT NULL,
        row_version rowversion NOT NULL,
        CONSTRAINT pk_visit PRIMARY KEY (id),
        CONSTRAINT fk_visit_hospital FOREIGN KEY (hospital_id)
            REFERENCES dbo.hospital (id),
        CONSTRAINT fk_visit_patient FOREIGN KEY (patient_id)
            REFERENCES dbo.patient (id)
    );
END;

IF OBJECT_ID(N'dbo.coding_task', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.coding_task
    (
        id uniqueidentifier NOT NULL,
        hospital_id uniqueidentifier NOT NULL,
        visit_id uniqueidentifier NOT NULL,
        pipeline_version nvarchar(64) NOT NULL,
        idempotency_key nvarchar(128) NULL,
        status int NOT NULL,
        retry_count int NOT NULL,
        started_at datetimeoffset(7) NULL,
        completed_at datetimeoffset(7) NULL,
        error_code nvarchar(128) NULL,
        created_at datetimeoffset(7) NOT NULL,
        updated_at datetimeoffset(7) NOT NULL,
        row_version rowversion NOT NULL,
        CONSTRAINT pk_coding_task PRIMARY KEY (id),
        CONSTRAINT fk_coding_task_hospital FOREIGN KEY (hospital_id)
            REFERENCES dbo.hospital (id),
        CONSTRAINT fk_coding_task_visit FOREIGN KEY (visit_id)
            REFERENCES dbo.visit (id)
    );
END;

IF OBJECT_ID(N'dbo.outbox_message', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.outbox_message
    (
        id uniqueidentifier NOT NULL,
        hospital_id uniqueidentifier NOT NULL,
        message_type nvarchar(256) NOT NULL,
        payload_json nvarchar(max) NOT NULL,
        occurred_at datetimeoffset(7) NOT NULL,
        published_at datetimeoffset(7) NULL,
        created_at datetimeoffset(7) NOT NULL,
        updated_at datetimeoffset(7) NOT NULL,
        row_version rowversion NOT NULL,
        CONSTRAINT pk_outbox_message PRIMARY KEY (id),
        CONSTRAINT fk_outbox_message_hospital FOREIGN KEY (hospital_id)
            REFERENCES dbo.hospital (id)
    );
END;

IF OBJECT_ID(N'dbo.inbox_message', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.inbox_message
    (
        id uniqueidentifier NOT NULL,
        hospital_id uniqueidentifier NOT NULL,
        message_id uniqueidentifier NOT NULL,
        consumer_name nvarchar(200) NOT NULL,
        received_at datetimeoffset(7) NOT NULL,
        processed_at datetimeoffset(7) NULL,
        created_at datetimeoffset(7) NOT NULL,
        updated_at datetimeoffset(7) NOT NULL,
        row_version rowversion NOT NULL,
        CONSTRAINT pk_inbox_message PRIMARY KEY (id),
        CONSTRAINT fk_inbox_message_hospital FOREIGN KEY (hospital_id)
            REFERENCES dbo.hospital (id)
    );
END;

IF OBJECT_ID(N'dbo.pipeline_trace', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.pipeline_trace
    (
        id uniqueidentifier NOT NULL,
        hospital_id uniqueidentifier NOT NULL,
        coding_task_id uniqueidentifier NULL,
        trace_id nvarchar(128) NOT NULL,
        status nvarchar(32) NOT NULL,
        started_at datetimeoffset(7) NOT NULL,
        completed_at datetimeoffset(7) NULL,
        created_at datetimeoffset(7) NOT NULL,
        updated_at datetimeoffset(7) NOT NULL,
        row_version rowversion NOT NULL,
        CONSTRAINT pk_pipeline_trace PRIMARY KEY (id),
        CONSTRAINT fk_pipeline_trace_hospital FOREIGN KEY (hospital_id)
            REFERENCES dbo.hospital (id),
        CONSTRAINT fk_pipeline_trace_coding_task FOREIGN KEY (coding_task_id)
            REFERENCES dbo.coding_task (id)
    );
END;

IF OBJECT_ID(N'dbo.pipeline_trace_step', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.pipeline_trace_step
    (
        id uniqueidentifier NOT NULL,
        hospital_id uniqueidentifier NOT NULL,
        pipeline_trace_id uniqueidentifier NOT NULL,
        step_name nvarchar(128) NOT NULL,
        status nvarchar(32) NOT NULL,
        started_at datetimeoffset(7) NOT NULL,
        completed_at datetimeoffset(7) NULL,
        error_code nvarchar(128) NULL,
        created_at datetimeoffset(7) NOT NULL,
        updated_at datetimeoffset(7) NOT NULL,
        row_version rowversion NOT NULL,
        CONSTRAINT pk_pipeline_trace_step PRIMARY KEY (id),
        CONSTRAINT fk_pipeline_trace_step_hospital FOREIGN KEY (hospital_id)
            REFERENCES dbo.hospital (id),
        CONSTRAINT fk_pipeline_trace_step_pipeline_trace FOREIGN KEY (pipeline_trace_id)
            REFERENCES dbo.pipeline_trace (id)
    );
END;

IF OBJECT_ID(N'dbo.audit_log', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.audit_log
    (
        id uniqueidentifier NOT NULL,
        hospital_id uniqueidentifier NOT NULL,
        resource_type nvarchar(128) NOT NULL,
        resource_id uniqueidentifier NOT NULL,
        action nvarchar(128) NOT NULL,
        result nvarchar(32) NOT NULL,
        request_id nvarchar(128) NOT NULL,
        actor_id nvarchar(128) NULL,
        created_at datetimeoffset(7) NOT NULL,
        updated_at datetimeoffset(7) NOT NULL,
        row_version rowversion NOT NULL,
        CONSTRAINT pk_audit_log PRIMARY KEY (id),
        CONSTRAINT fk_audit_log_hospital FOREIGN KEY (hospital_id)
            REFERENCES dbo.hospital (id)
    );
END;

IF COL_LENGTH(N'dbo.coding_task', N'idempotency_key') IS NULL
BEGIN
    ALTER TABLE dbo.coding_task
        ADD idempotency_key nvarchar(128) NULL;
END;

IF OBJECT_ID(N'dbo.medical_document', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.medical_document
    (
        id uniqueidentifier NOT NULL,
        hospital_id uniqueidentifier NOT NULL,
        visit_id uniqueidentifier NOT NULL,
        document_type nvarchar(128) NOT NULL,
        content_reference nvarchar(512) NOT NULL,
        content_hash nvarchar(128) NOT NULL,
        version int NOT NULL,
        created_at datetimeoffset(7) NOT NULL,
        updated_at datetimeoffset(7) NOT NULL,
        row_version rowversion NOT NULL,
        CONSTRAINT pk_medical_document PRIMARY KEY (id),
        CONSTRAINT fk_medical_document_hospital FOREIGN KEY (hospital_id)
            REFERENCES dbo.hospital (id),
        CONSTRAINT fk_medical_document_visit FOREIGN KEY (visit_id)
            REFERENCES dbo.visit (id)
    );
END;

-- 固定开发医院种子，不包含真实医疗数据；重复执行不会插入第二条。
IF NOT EXISTS (SELECT 1 FROM dbo.hospital WHERE code = N'DEV-HOSPITAL')
BEGIN
    INSERT INTO dbo.hospital
    (
        id,
        code,
        name,
        status,
        created_at,
        updated_at
    )
    VALUES
    (
        '00000000-0000-0000-0000-000000000001',
        N'DEV-HOSPITAL',
        N'Development Hospital',
        N'ACTIVE',
        TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'),
        TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00')
    );
END;

COMMIT TRANSACTION;
