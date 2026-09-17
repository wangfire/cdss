using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HospitalAi.Infrastructure.SqlServer;

/// <summary>
/// 平台业务数据库上下文。
/// </summary>
public sealed class HospitalAiDbContext(DbContextOptions<HospitalAiDbContext> options)
    : DbContext(options)
{
    public DbSet<HospitalRecord> Hospitals => Set<HospitalRecord>();

    public DbSet<PatientRecord> Patients => Set<PatientRecord>();

    public DbSet<VisitRecord> Visits => Set<VisitRecord>();

    public DbSet<CodingTaskRecord> CodingTasks => Set<CodingTaskRecord>();

    public DbSet<MedicalDocumentRecord> MedicalDocuments => Set<MedicalDocumentRecord>();

    public DbSet<OutboxMessageRecord> OutboxMessages => Set<OutboxMessageRecord>();

    public DbSet<InboxMessageRecord> InboxMessages => Set<InboxMessageRecord>();

    public DbSet<PipelineTraceRecord> PipelineTraces => Set<PipelineTraceRecord>();

    public DbSet<PipelineTraceStepRecord> PipelineTraceSteps => Set<PipelineTraceStepRecord>();

    public DbSet<AuditLogRecord> AuditLogs => Set<AuditLogRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureHospital(modelBuilder);
        ConfigurePatient(modelBuilder);
        ConfigureVisit(modelBuilder);
        ConfigureCodingTask(modelBuilder);
        ConfigureMedicalDocument(modelBuilder);
        ConfigureOutbox(modelBuilder);
        ConfigureInbox(modelBuilder);
        ConfigurePipelineTrace(modelBuilder);
        ConfigurePipelineTraceStep(modelBuilder);
        ConfigureAuditLog(modelBuilder);
    }

    private static void ConfigureHospital(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<HospitalRecord>();

        entity.ToTable("hospital");
        ConfigureCommonProperties(entity);
        entity.Property(item => item.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
        entity.Property(item => item.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        entity.Property(item => item.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        entity.HasIndex(item => item.Code)
            .HasDatabaseName("ux_hospital_code")
            .IsUnique();
    }

    private static void ConfigurePatient(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PatientRecord>();

        entity.ToTable("patient");
        ConfigureCommonProperties(entity);
        entity.Property(item => item.HospitalId).HasColumnName("hospital_id").IsRequired();
        entity.Property(item => item.SourceSystem).HasColumnName("source_system").HasMaxLength(64).IsRequired();
        entity.Property(item => item.SourcePatientId).HasColumnName("source_patient_id").HasMaxLength(128).IsRequired();
        entity.Property(item => item.DisplayName).HasColumnName("display_name").HasMaxLength(200);
        entity.HasIndex(item => new { item.HospitalId, item.SourceSystem, item.SourcePatientId })
            .HasDatabaseName("ux_patient_hospital_source")
            .IsUnique();
        entity.HasOne(item => item.Hospital)
            .WithMany(item => item.Patients)
            .HasForeignKey(item => item.HospitalId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureVisit(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<VisitRecord>();

        entity.ToTable("visit");
        ConfigureCommonProperties(entity);
        entity.Property(item => item.HospitalId).HasColumnName("hospital_id").IsRequired();
        entity.Property(item => item.PatientId).HasColumnName("patient_id").IsRequired();
        entity.Property(item => item.AdmissionAt).HasColumnName("admission_at").IsRequired();
        entity.Property(item => item.DischargeAt).HasColumnName("discharge_at");
        entity.HasOne(item => item.Hospital)
            .WithMany(item => item.Visits)
            .HasForeignKey(item => item.HospitalId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(item => item.Patient)
            .WithMany(item => item.Visits)
            .HasForeignKey(item => item.PatientId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureCodingTask(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<CodingTaskRecord>();

        entity.ToTable("coding_task");
        ConfigureCommonProperties(entity);
        entity.Property(item => item.HospitalId).HasColumnName("hospital_id").IsRequired();
        entity.Property(item => item.VisitId).HasColumnName("visit_id").IsRequired();
        entity.Property(item => item.PipelineVersion).HasColumnName("pipeline_version").HasMaxLength(64).IsRequired();
        entity.Property(item => item.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(128);
        entity.Property(item => item.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        entity.Property(item => item.RetryCount).HasColumnName("retry_count").IsRequired();
        entity.Property(item => item.StartedAt).HasColumnName("started_at");
        entity.Property(item => item.CompletedAt).HasColumnName("completed_at");
        entity.Property(item => item.ErrorCode).HasColumnName("error_code").HasMaxLength(128);
        entity.HasIndex(item => new { item.HospitalId, item.VisitId, item.PipelineVersion })
            .HasDatabaseName("ux_coding_task_hospital_visit_pipeline")
            .IsUnique();
        entity.HasIndex(item => new { item.HospitalId, item.IdempotencyKey })
            .HasDatabaseName("ux_coding_task_hospital_idempotency_key")
            .IsUnique()
            .HasFilter("[idempotency_key] IS NOT NULL");
        entity.HasOne(item => item.Hospital)
            .WithMany(item => item.CodingTasks)
            .HasForeignKey(item => item.HospitalId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(item => item.Visit)
            .WithMany(item => item.CodingTasks)
            .HasForeignKey(item => item.VisitId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureMedicalDocument(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<MedicalDocumentRecord>();

        entity.ToTable("medical_document");
        ConfigureCommonProperties(entity);
        entity.Property(item => item.HospitalId).HasColumnName("hospital_id").IsRequired();
        entity.Property(item => item.VisitId).HasColumnName("visit_id").IsRequired();
        entity.Property(item => item.DocumentType).HasColumnName("document_type").HasMaxLength(128).IsRequired();
        entity.Property(item => item.ContentReference).HasColumnName("content_reference").HasMaxLength(512).IsRequired();
        entity.Property(item => item.ContentHash).HasColumnName("content_hash").HasMaxLength(128).IsRequired();
        entity.Property(item => item.Version).HasColumnName("version").IsRequired();
        entity.HasIndex(item => new { item.HospitalId, item.VisitId, item.DocumentType, item.Version })
            .HasDatabaseName("ux_medical_document_visit_type_version")
            .IsUnique();
        entity.HasOne(item => item.Hospital)
            .WithMany()
            .HasForeignKey(item => item.HospitalId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(item => item.Visit)
            .WithMany()
            .HasForeignKey(item => item.VisitId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureOutbox(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<OutboxMessageRecord>();

        entity.ToTable("outbox_message");
        ConfigureCommonProperties(entity);
        entity.Property(item => item.HospitalId).HasColumnName("hospital_id").IsRequired();
        entity.Property(item => item.MessageType).HasColumnName("message_type").HasMaxLength(256).IsRequired();
        entity.Property(item => item.PayloadJson).HasColumnName("payload_json").HasColumnType("nvarchar(max)").IsRequired();
        entity.Property(item => item.OccurredAt).HasColumnName("occurred_at").IsRequired();
        entity.Property(item => item.PublishedAt).HasColumnName("published_at");
        entity.HasIndex(item => new { item.PublishedAt, item.OccurredAt })
            .HasDatabaseName("ix_outbox_message_pending");
        entity.HasOne(item => item.Hospital)
            .WithMany()
            .HasForeignKey(item => item.HospitalId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureInbox(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<InboxMessageRecord>();

        entity.ToTable("inbox_message");
        ConfigureCommonProperties(entity);
        entity.Property(item => item.HospitalId).HasColumnName("hospital_id").IsRequired();
        entity.Property(item => item.MessageId).HasColumnName("message_id").IsRequired();
        entity.Property(item => item.ConsumerName).HasColumnName("consumer_name").HasMaxLength(200).IsRequired();
        entity.Property(item => item.ReceivedAt).HasColumnName("received_at").IsRequired();
        entity.Property(item => item.ProcessedAt).HasColumnName("processed_at");
        entity.HasIndex(item => new { item.MessageId, item.ConsumerName })
            .HasDatabaseName("ux_inbox_message_message_consumer")
            .IsUnique();
        entity.HasOne(item => item.Hospital)
            .WithMany()
            .HasForeignKey(item => item.HospitalId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigurePipelineTrace(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PipelineTraceRecord>();

        entity.ToTable("pipeline_trace");
        ConfigureCommonProperties(entity);
        entity.Property(item => item.HospitalId).HasColumnName("hospital_id").IsRequired();
        entity.Property(item => item.CodingTaskId).HasColumnName("coding_task_id");
        entity.Property(item => item.TraceId).HasColumnName("trace_id").HasMaxLength(128).IsRequired();
        entity.Property(item => item.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        entity.Property(item => item.StartedAt).HasColumnName("started_at").IsRequired();
        entity.Property(item => item.CompletedAt).HasColumnName("completed_at");
        entity.HasIndex(item => item.TraceId)
            .HasDatabaseName("ux_pipeline_trace_trace_id")
            .IsUnique();
        entity.HasOne(item => item.Hospital)
            .WithMany()
            .HasForeignKey(item => item.HospitalId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(item => item.CodingTask)
            .WithMany(item => item.PipelineTraces)
            .HasForeignKey(item => item.CodingTaskId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigurePipelineTraceStep(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PipelineTraceStepRecord>();

        entity.ToTable("pipeline_trace_step");
        ConfigureCommonProperties(entity);
        entity.Property(item => item.HospitalId).HasColumnName("hospital_id").IsRequired();
        entity.Property(item => item.PipelineTraceId).HasColumnName("pipeline_trace_id").IsRequired();
        entity.Property(item => item.StepName).HasColumnName("step_name").HasMaxLength(128).IsRequired();
        entity.Property(item => item.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        entity.Property(item => item.StartedAt).HasColumnName("started_at").IsRequired();
        entity.Property(item => item.CompletedAt).HasColumnName("completed_at");
        entity.Property(item => item.ErrorCode).HasColumnName("error_code").HasMaxLength(128);
        entity.HasIndex(item => new { item.PipelineTraceId, item.StepName })
            .HasDatabaseName("ux_pipeline_trace_step_trace_step")
            .IsUnique();
        entity.HasOne(item => item.Hospital)
            .WithMany()
            .HasForeignKey(item => item.HospitalId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(item => item.PipelineTrace)
            .WithMany(item => item.Steps)
            .HasForeignKey(item => item.PipelineTraceId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureAuditLog(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AuditLogRecord>();

        entity.ToTable("audit_log");
        ConfigureCommonProperties(entity);
        entity.Property(item => item.HospitalId).HasColumnName("hospital_id").IsRequired();
        entity.Property(item => item.ResourceType).HasColumnName("resource_type").HasMaxLength(128).IsRequired();
        entity.Property(item => item.ResourceId).HasColumnName("resource_id").IsRequired();
        entity.Property(item => item.Action).HasColumnName("action").HasMaxLength(128).IsRequired();
        entity.Property(item => item.Result).HasColumnName("result").HasMaxLength(32).IsRequired();
        entity.Property(item => item.RequestId).HasColumnName("request_id").HasMaxLength(128).IsRequired();
        entity.Property(item => item.ActorId).HasColumnName("actor_id").HasMaxLength(128);
        entity.HasIndex(item => new { item.HospitalId, item.CreatedAt })
            .HasDatabaseName("ix_audit_log_hospital_created");
        entity.HasOne(item => item.Hospital)
            .WithMany()
            .HasForeignKey(item => item.HospitalId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureCommonProperties<TEntity>(
        EntityTypeBuilder<TEntity> entity)
        where TEntity : class
    {
        // 所有持久化记录都使用同名 Guid 主键，统一配置可避免重复映射代码。
        entity.HasKey("Id");

        entity.Property<byte[]>("RowVersion")
            .HasColumnName("row_version")
            .HasColumnType("rowversion")
            .IsRowVersion()
            .IsConcurrencyToken()
            .ValueGeneratedOnAddOrUpdate();

        entity.Property<DateTimeOffset>("CreatedAt")
            .HasColumnName("created_at")
            .HasColumnType("datetimeoffset(7)")
            .IsRequired();

        entity.Property<DateTimeOffset>("UpdatedAt")
            .HasColumnName("updated_at")
            .HasColumnType("datetimeoffset(7)")
            .IsRequired();

        entity.Property<Guid>("Id")
            .HasColumnName("id")
            .HasColumnType("uniqueidentifier")
            .ValueGeneratedNever();
    }
}
