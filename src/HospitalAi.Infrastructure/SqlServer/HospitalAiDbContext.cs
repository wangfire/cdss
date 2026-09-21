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

    public DbSet<AppUserRecord> AppUsers => Set<AppUserRecord>();

    public DbSet<AppRoleRecord> AppRoles => Set<AppRoleRecord>();

    public DbSet<AppUserRoleRecord> AppUserRoles => Set<AppUserRoleRecord>();

    public DbSet<PatientRecord> Patients => Set<PatientRecord>();

    public DbSet<VisitRecord> Visits => Set<VisitRecord>();

    public DbSet<CodingTaskRecord> CodingTasks => Set<CodingTaskRecord>();

    public DbSet<MedicalDocumentRecord> MedicalDocuments => Set<MedicalDocumentRecord>();

    public DbSet<CodeSystemRecord> CodeSystems => Set<CodeSystemRecord>();

    public DbSet<MedicalCodeRecord> MedicalCodes => Set<MedicalCodeRecord>();

    public DbSet<TermSynonymRecord> TermSynonyms => Set<TermSynonymRecord>();

    public DbSet<CodingRuleRecord> CodingRules => Set<CodingRuleRecord>();

    public DbSet<DocumentSectionRecord> DocumentSections => Set<DocumentSectionRecord>();

    public DbSet<ClinicalEntityRecord> ClinicalEntities => Set<ClinicalEntityRecord>();

    public DbSet<CodingRecommendationRecord> CodingRecommendations => Set<CodingRecommendationRecord>();

    public DbSet<RecommendationEvidenceRecord> RecommendationEvidences => Set<RecommendationEvidenceRecord>();

    public DbSet<CodingReviewRecord> CodingReviews => Set<CodingReviewRecord>();

    public DbSet<FinalCodingResultRecord> FinalCodingResults => Set<FinalCodingResultRecord>();

    public DbSet<OutboxMessageRecord> OutboxMessages => Set<OutboxMessageRecord>();

    public DbSet<InboxMessageRecord> InboxMessages => Set<InboxMessageRecord>();

    public DbSet<PipelineTraceRecord> PipelineTraces => Set<PipelineTraceRecord>();

    public DbSet<PipelineTraceStepRecord> PipelineTraceSteps => Set<PipelineTraceStepRecord>();

    public DbSet<AuditLogRecord> AuditLogs => Set<AuditLogRecord>();

    public DbSet<ClinicalFactRecord> ClinicalFacts => Set<ClinicalFactRecord>();

    public DbSet<ClinicalEvidenceRecord> ClinicalEvidences => Set<ClinicalEvidenceRecord>();

    public DbSet<ClinicalFactEvidenceRecord> ClinicalFactEvidences => Set<ClinicalFactEvidenceRecord>();

    public DbSet<CodingDiagnosisInputRecord> CodingDiagnosisInputs => Set<CodingDiagnosisInputRecord>();

    public DbSet<CodingCandidateRecord> CodingCandidates => Set<CodingCandidateRecord>();

    public DbSet<RecommendationScoreRecord> RecommendationScores => Set<RecommendationScoreRecord>();

    public DbSet<QualityIssueRecord> QualityIssues => Set<QualityIssueRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureHospital(modelBuilder);
        ConfigureAppUser(modelBuilder);
        ConfigureAppRole(modelBuilder);
        ConfigureAppUserRole(modelBuilder);
        ConfigurePatient(modelBuilder);
        ConfigureVisit(modelBuilder);
        ConfigureCodingTask(modelBuilder);
        ConfigureMedicalDocument(modelBuilder);
        ConfigureCodeSystem(modelBuilder);
        ConfigureMedicalCode(modelBuilder);
        ConfigureTermSynonym(modelBuilder);
        ConfigureCodingRule(modelBuilder);
        ConfigureDocumentSection(modelBuilder);
        ConfigureClinicalEntity(modelBuilder);
        ConfigureClinicalFact(modelBuilder);
        ConfigureClinicalEvidence(modelBuilder);
        ConfigureClinicalFactEvidence(modelBuilder);
        ConfigureCodingDiagnosisInput(modelBuilder);
        ConfigureCodingCandidate(modelBuilder);
        ConfigureRecommendationScore(modelBuilder);
        ConfigureQualityIssue(modelBuilder);
        ConfigureCodingRecommendation(modelBuilder);
        ConfigureRecommendationEvidence(modelBuilder);
        ConfigureCodingReview(modelBuilder);
        ConfigureFinalCodingResult(modelBuilder);
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

    private static void ConfigureAppUser(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AppUserRecord>();

        entity.ToTable("app_user");
        ConfigureCommonProperties(entity);
        entity.Property(item => item.HospitalId).HasColumnName("hospital_id").IsRequired();
        entity.Property(item => item.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
        entity.Property(item => item.DisplayName).HasColumnName("display_name").HasMaxLength(200).IsRequired();
        entity.Property(item => item.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        entity.Property(item => item.PasswordHash).HasColumnName("password_hash").HasMaxLength(512).IsRequired();
        entity.Property(item => item.LastLoginAt).HasColumnName("last_login_at");
        entity.HasIndex(item => new { item.HospitalId, item.Code })
            .HasDatabaseName("ux_app_user_hospital_code")
            .IsUnique();
        entity.HasOne(item => item.Hospital)
            .WithMany()
            .HasForeignKey(item => item.HospitalId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureAppRole(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AppRoleRecord>();

        entity.ToTable("app_role");
        ConfigureCommonProperties(entity);
        entity.Property(item => item.HospitalId).HasColumnName("hospital_id").IsRequired();
        entity.Property(item => item.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
        entity.Property(item => item.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        entity.Property(item => item.Description).HasColumnName("description").HasMaxLength(500).IsRequired();
        entity.HasIndex(item => new { item.HospitalId, item.Code })
            .HasDatabaseName("ux_app_role_hospital_code")
            .IsUnique();
        entity.HasOne(item => item.Hospital)
            .WithMany()
            .HasForeignKey(item => item.HospitalId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureAppUserRole(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AppUserRoleRecord>();

        entity.ToTable("app_user_role");
        ConfigureCommonProperties(entity);
        entity.Property(item => item.HospitalId).HasColumnName("hospital_id").IsRequired();
        entity.Property(item => item.AppUserId).HasColumnName("app_user_id").IsRequired();
        entity.Property(item => item.AppRoleId).HasColumnName("app_role_id").IsRequired();
        entity.HasIndex(item => new { item.HospitalId, item.AppUserId, item.AppRoleId })
            .HasDatabaseName("ux_app_user_role_hospital_user_role")
            .IsUnique();
        entity.HasOne(item => item.Hospital)
            .WithMany()
            .HasForeignKey(item => item.HospitalId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(item => item.AppUser)
            .WithMany()
            .HasForeignKey(item => item.AppUserId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(item => item.AppRole)
            .WithMany()
            .HasForeignKey(item => item.AppRoleId)
            .OnDelete(DeleteBehavior.Restrict);
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
        entity.Property(item => item.CodingStage).HasColumnName("coding_stage").HasConversion<int>().IsRequired();
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
        entity.Property(item => item.ParseVersion).HasColumnName("parse_version").HasMaxLength(64);
        entity.Property(item => item.OcrVersion).HasColumnName("ocr_version").HasMaxLength(64);
        entity.Property(item => item.DocumentStatus).HasColumnName("document_status").HasMaxLength(32).IsRequired();
        entity.Property(item => item.SourceDocumentId).HasColumnName("source_document_id");
        entity.Property(item => item.SourceUpdatedAt).HasColumnName("source_updated_at");
        entity.Property(item => item.IsCurrent).HasColumnName("is_current").IsRequired();
        entity.HasIndex(item => new { item.HospitalId, item.VisitId, item.DocumentType, item.Version })
            .HasDatabaseName("ux_medical_document_visit_type_version")
            .IsUnique();
        entity.HasIndex(item => new { item.HospitalId, item.VisitId, item.DocumentType, item.IsCurrent })
            .HasDatabaseName("ix_medical_document_visit_type_current");
        entity.HasOne(item => item.Hospital)
            .WithMany()
            .HasForeignKey(item => item.HospitalId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(item => item.Visit)
            .WithMany()
            .HasForeignKey(item => item.VisitId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureCodeSystem(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<CodeSystemRecord>();

        entity.ToTable("code_system");
        ConfigureCommonProperties(entity);
        entity.Property(item => item.HospitalId).HasColumnName("hospital_id").IsRequired();
        entity.Property(item => item.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
        entity.Property(item => item.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        entity.Property(item => item.Version).HasColumnName("version").HasMaxLength(64).IsRequired();
        entity.HasIndex(item => new { item.HospitalId, item.Code })
            .HasDatabaseName("ux_code_system_hospital_code")
            .IsUnique();
        entity.HasOne(item => item.Hospital)
            .WithMany(item => item.CodeSystems)
            .HasForeignKey(item => item.HospitalId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureMedicalCode(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<MedicalCodeRecord>();

        entity.ToTable("medical_code");
        ConfigureCommonProperties(entity);
        entity.Property(item => item.HospitalId).HasColumnName("hospital_id").IsRequired();
        entity.Property(item => item.CodeSystemId).HasColumnName("code_system_id").IsRequired();
        entity.Property(item => item.CodeSystemCode).HasColumnName("code_system_code").HasMaxLength(64).IsRequired();
        entity.Property(item => item.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
        entity.Property(item => item.Title).HasColumnName("title").HasMaxLength(300).IsRequired();
        entity.Property(item => item.CodeType).HasColumnName("code_type").HasMaxLength(64).IsRequired();
        entity.Property(item => item.SearchText).HasColumnName("search_text").HasMaxLength(1000).IsRequired();
        entity.Property(item => item.IsEnabled).HasColumnName("is_enabled").IsRequired();
        entity.HasIndex(item => new { item.HospitalId, item.CodeSystemCode, item.Code })
            .HasDatabaseName("ux_medical_code_hospital_system_code")
            .IsUnique();
        entity.HasIndex(item => new { item.HospitalId, item.CodeSystemCode, item.CodeType })
            .HasDatabaseName("ix_medical_code_hospital_system_type");
        entity.HasOne(item => item.Hospital)
            .WithMany()
            .HasForeignKey(item => item.HospitalId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(item => item.CodeSystem)
            .WithMany(item => item.MedicalCodes)
            .HasForeignKey(item => item.CodeSystemId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureTermSynonym(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<TermSynonymRecord>();

        entity.ToTable("term_synonym");
        ConfigureCommonProperties(entity);
        entity.Property(item => item.HospitalId).HasColumnName("hospital_id").IsRequired();
        entity.Property(item => item.Term).HasColumnName("term").HasMaxLength(200).IsRequired();
        entity.Property(item => item.NormalizedTerm).HasColumnName("normalized_term").HasMaxLength(200).IsRequired();
        entity.Property(item => item.CodeSystemCode).HasColumnName("code_system_code").HasMaxLength(64).IsRequired();
        entity.Property(item => item.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
        entity.Property(item => item.EntityType).HasColumnName("entity_type").HasMaxLength(64).IsRequired();
        entity.HasIndex(item => new { item.HospitalId, item.Term, item.EntityType, item.CodeSystemCode, item.Code })
            .HasDatabaseName("ux_term_synonym_hospital_term_type_code")
            .IsUnique();
        entity.HasOne(item => item.Hospital)
            .WithMany()
            .HasForeignKey(item => item.HospitalId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureCodingRule(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<CodingRuleRecord>();

        entity.ToTable("coding_rule");
        ConfigureCommonProperties(entity);
        entity.Property(item => item.HospitalId).HasColumnName("hospital_id").IsRequired();
        entity.Property(item => item.RuleCode).HasColumnName("rule_code").HasMaxLength(128).IsRequired();
        entity.Property(item => item.RuleVersion).HasColumnName("rule_version").HasMaxLength(64).IsRequired();
        entity.Property(item => item.CodeSystemCode).HasColumnName("code_system_code").HasMaxLength(64).IsRequired();
        entity.Property(item => item.CodePattern).HasColumnName("code_pattern").HasMaxLength(128).IsRequired();
        entity.Property(item => item.RuleType).HasColumnName("rule_type").HasMaxLength(64).IsRequired();
        entity.Property(item => item.Severity).HasColumnName("severity").HasMaxLength(32).IsRequired();
        entity.Property(item => item.Message).HasColumnName("message").HasMaxLength(500).IsRequired();
        entity.Property(item => item.Priority).HasColumnName("priority").IsRequired();
        entity.Property(item => item.RuleGroup).HasColumnName("rule_group").HasMaxLength(128);
        entity.Property(item => item.ConditionJson).HasColumnName("condition_json").HasColumnType("nvarchar(max)");
        entity.Property(item => item.ActionJson).HasColumnName("action_json").HasColumnType("nvarchar(max)");
        entity.Property(item => item.Blocking).HasColumnName("blocking").IsRequired();
        entity.Property(item => item.IsBuiltin).HasColumnName("is_builtin").IsRequired();
        entity.Property(item => item.IsEnabled).HasColumnName("is_enabled").IsRequired();
        entity.HasIndex(item => new { item.HospitalId, item.RuleCode, item.RuleVersion })
            .HasDatabaseName("ux_coding_rule_hospital_rule_version")
            .IsUnique();
        entity.HasOne(item => item.Hospital)
            .WithMany()
            .HasForeignKey(item => item.HospitalId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureDocumentSection(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<DocumentSectionRecord>();

        entity.ToTable("document_section");
        ConfigureCommonProperties(entity);
        entity.Property(item => item.HospitalId).HasColumnName("hospital_id").IsRequired();
        entity.Property(item => item.VisitId).HasColumnName("visit_id").IsRequired();
        entity.Property(item => item.MedicalDocumentId).HasColumnName("medical_document_id").IsRequired();
        entity.Property(item => item.SectionType).HasColumnName("section_type").HasMaxLength(64).IsRequired();
        entity.Property(item => item.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        entity.Property(item => item.Content).HasColumnName("content").HasColumnType("nvarchar(max)").IsRequired();
        entity.Property(item => item.Sequence).HasColumnName("sequence").IsRequired();
        entity.Property(item => item.StartPosition).HasColumnName("start_position").IsRequired();
        entity.Property(item => item.EndPosition).HasColumnName("end_position").IsRequired();
        entity.Property(item => item.TokenCount).HasColumnName("token_count").IsRequired();
        entity.Property(item => item.ContentHash).HasColumnName("content_hash").HasMaxLength(128).IsRequired();
        entity.Property(item => item.EmbeddingStatus).HasColumnName("embedding_status").HasMaxLength(32).IsRequired();
        entity.Property(item => item.IndexStatus).HasColumnName("index_status").HasMaxLength(32).IsRequired();
        entity.HasIndex(item => new { item.HospitalId, item.VisitId, item.Sequence })
            .HasDatabaseName("ix_document_section_visit_sequence");
        entity.HasIndex(item => new { item.MedicalDocumentId, item.ContentHash })
            .HasDatabaseName("ix_document_section_document_hash");
        entity.HasOne(item => item.Hospital)
            .WithMany()
            .HasForeignKey(item => item.HospitalId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(item => item.Visit)
            .WithMany()
            .HasForeignKey(item => item.VisitId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(item => item.MedicalDocument)
            .WithMany()
            .HasForeignKey(item => item.MedicalDocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureClinicalEntity(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ClinicalEntityRecord>();

        entity.ToTable("clinical_entity");
        ConfigureCommonProperties(entity);
        entity.Property(item => item.HospitalId).HasColumnName("hospital_id").IsRequired();
        entity.Property(item => item.CodingTaskId).HasColumnName("coding_task_id").IsRequired();
        entity.Property(item => item.DocumentSectionId).HasColumnName("document_section_id");
        entity.Property(item => item.EntityType).HasColumnName("entity_type").HasMaxLength(64).IsRequired();
        entity.Property(item => item.RawText).HasColumnName("raw_text").HasMaxLength(300).IsRequired();
        entity.Property(item => item.NormalizedText).HasColumnName("normalized_text").HasMaxLength(300).IsRequired();
        entity.Property(item => item.IsNegated).HasColumnName("is_negated").IsRequired();
        entity.HasIndex(item => new { item.HospitalId, item.CodingTaskId, item.EntityType })
            .HasDatabaseName("ix_clinical_entity_task_type");
        entity.HasOne(item => item.Hospital)
            .WithMany()
            .HasForeignKey(item => item.HospitalId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(item => item.CodingTask)
            .WithMany()
            .HasForeignKey(item => item.CodingTaskId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(item => item.DocumentSection)
            .WithMany()
            .HasForeignKey(item => item.DocumentSectionId)
            .OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureClinicalFact(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ClinicalFactRecord>();

        entity.ToTable("clinical_fact");
        ConfigureCommonProperties(entity);
        entity.Property(item => item.HospitalId).HasColumnName("hospital_id").IsRequired();
        entity.Property(item => item.VisitId).HasColumnName("visit_id").IsRequired();
        entity.Property(item => item.CodingTaskId).HasColumnName("coding_task_id").IsRequired();
        entity.Property(item => item.PipelineRunId).HasColumnName("pipeline_run_id").IsRequired();
        entity.Property(item => item.FactType).HasColumnName("fact_type").HasConversion<int>().IsRequired();
        entity.Property(item => item.FactName).HasColumnName("fact_name").HasMaxLength(300).IsRequired();
        entity.Property(item => item.NormalizedValue).HasColumnName("normalized_value").HasMaxLength(300);
        entity.Property(item => item.OriginalValue).HasColumnName("original_value").HasMaxLength(500).IsRequired();
        entity.Property(item => item.Negation).HasColumnName("negation").IsRequired();
        entity.Property(item => item.Certainty).HasColumnName("certainty").HasConversion<int>().IsRequired();
        entity.Property(item => item.Temporality).HasColumnName("temporality").HasConversion<int>().IsRequired();
        entity.Property(item => item.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        entity.Property(item => item.Confidence).HasColumnName("confidence").HasColumnType("decimal(5,4)").IsRequired();
        entity.Property(item => item.SourceDocumentId).HasColumnName("source_document_id");
        entity.Property(item => item.SourceSectionId).HasColumnName("source_section_id");
        entity.Property(item => item.SourceStart).HasColumnName("source_start");
        entity.Property(item => item.SourceEnd).HasColumnName("source_end");
        entity.Property(item => item.ExtractorVersion).HasColumnName("extractor_version").HasMaxLength(64).IsRequired();
        entity.HasIndex(item => new { item.HospitalId, item.VisitId })
            .HasDatabaseName("ix_clinical_fact_hospital_visit");
        entity.HasIndex(item => new { item.CodingTaskId, item.PipelineRunId })
            .HasDatabaseName("ix_clinical_fact_task_run");
    }

    private static void ConfigureClinicalEvidence(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ClinicalEvidenceRecord>();

        entity.ToTable("clinical_evidence");
        ConfigureCommonProperties(entity);
        entity.Property(item => item.HospitalId).HasColumnName("hospital_id").IsRequired();
        entity.Property(item => item.VisitId).HasColumnName("visit_id").IsRequired();
        entity.Property(item => item.CodingTaskId).HasColumnName("coding_task_id").IsRequired();
        entity.Property(item => item.PipelineRunId).HasColumnName("pipeline_run_id").IsRequired();
        entity.Property(item => item.EvidenceType).HasColumnName("evidence_type").HasMaxLength(64).IsRequired();
        entity.Property(item => item.SourceType).HasColumnName("source_type").HasMaxLength(64).IsRequired();
        entity.Property(item => item.DocumentId).HasColumnName("document_id");
        entity.Property(item => item.SectionId).HasColumnName("section_id");
        entity.Property(item => item.OriginalText).HasColumnName("original_text").HasMaxLength(1000).IsRequired();
        entity.Property(item => item.StartPosition).HasColumnName("start_position").IsRequired();
        entity.Property(item => item.EndPosition).HasColumnName("end_position").IsRequired();
        entity.Property(item => item.EvidenceLevel).HasColumnName("evidence_level").HasConversion<int>().IsRequired();
        entity.Property(item => item.SourceReliability).HasColumnName("source_reliability").HasColumnType("decimal(5,4)").IsRequired();
        entity.Property(item => item.TemporalValidity).HasColumnName("temporal_validity").HasColumnType("decimal(5,4)").IsRequired();
        entity.Property(item => item.TextCompleteness).HasColumnName("text_completeness").HasColumnType("decimal(5,4)").IsRequired();
        entity.Property(item => item.EvidenceScore).HasColumnName("evidence_score").HasColumnType("decimal(5,4)").IsRequired();
        entity.Property(item => item.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        entity.HasIndex(item => new { item.HospitalId, item.VisitId })
            .HasDatabaseName("ix_clinical_evidence_hospital_visit");
        entity.HasIndex(item => new { item.CodingTaskId, item.PipelineRunId })
            .HasDatabaseName("ix_clinical_evidence_task_run");
    }

    private static void ConfigureClinicalFactEvidence(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ClinicalFactEvidenceRecord>();

        entity.ToTable("clinical_fact_evidence");
        ConfigureCommonProperties(entity);
        entity.Property(item => item.HospitalId).HasColumnName("hospital_id").IsRequired();
        entity.Property(item => item.FactId).HasColumnName("fact_id").IsRequired();
        entity.Property(item => item.EvidenceId).HasColumnName("evidence_id").IsRequired();
        entity.HasIndex(item => new { item.HospitalId, item.FactId, item.EvidenceId })
            .HasDatabaseName("ux_clinical_fact_evidence_fact_evidence")
            .IsUnique();
    }

    private static void ConfigureCodingDiagnosisInput(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<CodingDiagnosisInputRecord>();

        entity.ToTable("coding_diagnosis_input");
        ConfigureCommonProperties(entity);
        entity.Property(item => item.HospitalId).HasColumnName("hospital_id").IsRequired();
        entity.Property(item => item.VisitId).HasColumnName("visit_id").IsRequired();
        entity.Property(item => item.CodingTaskId).HasColumnName("coding_task_id").IsRequired();
        entity.Property(item => item.SourceType).HasColumnName("source_type").HasMaxLength(32).IsRequired();
        entity.Property(item => item.OriginalText).HasColumnName("original_text").HasMaxLength(500).IsRequired();
        entity.Property(item => item.NormalizedText).HasColumnName("normalized_text").HasMaxLength(500);
        entity.Property(item => item.IsPrincipal).HasColumnName("is_principal").IsRequired();
        entity.Property(item => item.DiagnosisOrder).HasColumnName("diagnosis_order").IsRequired();
        entity.Property(item => item.SourceDocumentId).HasColumnName("source_document_id");
        entity.Property(item => item.SourceSectionId).HasColumnName("source_section_id");
        entity.Property(item => item.SourceStart).HasColumnName("source_start");
        entity.Property(item => item.SourceEnd).HasColumnName("source_end");
        entity.Property(item => item.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        entity.HasIndex(item => new { item.HospitalId, item.CodingTaskId, item.DiagnosisOrder })
            .HasDatabaseName("ix_coding_diagnosis_input_task_order");
    }

    private static void ConfigureCodingCandidate(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<CodingCandidateRecord>();

        entity.ToTable("coding_candidate");
        ConfigureCommonProperties(entity);
        entity.Property(item => item.HospitalId).HasColumnName("hospital_id").IsRequired();
        entity.Property(item => item.CodingTaskId).HasColumnName("coding_task_id").IsRequired();
        entity.Property(item => item.DiagnosisInputId).HasColumnName("diagnosis_input_id").IsRequired();
        entity.Property(item => item.PipelineRunId).HasColumnName("pipeline_run_id").IsRequired();
        entity.Property(item => item.CodeSystem).HasColumnName("code_system").HasMaxLength(64).IsRequired();
        entity.Property(item => item.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
        entity.Property(item => item.Title).HasColumnName("title").HasMaxLength(300).IsRequired();
        entity.Property(item => item.RecallSource).HasColumnName("recall_source").HasMaxLength(32).IsRequired();
        entity.Property(item => item.ExactScore).HasColumnName("exact_score").HasColumnType("decimal(5,4)");
        entity.Property(item => item.Bm25Score).HasColumnName("bm25_score").HasColumnType("decimal(5,4)");
        entity.Property(item => item.VectorScore).HasColumnName("vector_score").HasColumnType("decimal(5,4)");
        entity.Property(item => item.RerankScore).HasColumnName("rerank_score").HasColumnType("decimal(5,4)");
        entity.Property(item => item.RuleScore).HasColumnName("rule_score").HasColumnType("decimal(5,4)");
        entity.Property(item => item.EvidenceScore).HasColumnName("evidence_score").HasColumnType("decimal(5,4)");
        entity.Property(item => item.FinalScore).HasColumnName("final_score").HasColumnType("decimal(5,4)").IsRequired();
        entity.Property(item => item.Rank).HasColumnName("rank").IsRequired();
        entity.Property(item => item.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        entity.HasIndex(item => new { item.HospitalId, item.DiagnosisInputId, item.PipelineRunId })
            .HasDatabaseName("ix_coding_candidate_input_run");
    }

    private static void ConfigureRecommendationScore(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<RecommendationScoreRecord>();

        entity.ToTable("recommendation_score");
        ConfigureCommonProperties(entity);
        entity.Property(item => item.HospitalId).HasColumnName("hospital_id").IsRequired();
        entity.Property(item => item.RecommendationId).HasColumnName("recommendation_id").IsRequired();
        entity.Property(item => item.ExactScore).HasColumnName("exact_score").HasColumnType("decimal(5,4)");
        entity.Property(item => item.SemanticScore).HasColumnName("semantic_score").HasColumnType("decimal(5,4)");
        entity.Property(item => item.RetrievalScore).HasColumnName("retrieval_score").HasColumnType("decimal(5,4)");
        entity.Property(item => item.RerankScore).HasColumnName("rerank_score").HasColumnType("decimal(5,4)");
        entity.Property(item => item.RuleScore).HasColumnName("rule_score").HasColumnType("decimal(5,4)");
        entity.Property(item => item.EvidenceScore).HasColumnName("evidence_score").HasColumnType("decimal(5,4)");
        entity.Property(item => item.LlmScore).HasColumnName("llm_score").HasColumnType("decimal(5,4)");
        entity.Property(item => item.MarginScore).HasColumnName("margin_score").HasColumnType("decimal(5,4)");
        entity.Property(item => item.ScoreProfile).HasColumnName("score_profile").HasMaxLength(64).IsRequired();
        entity.HasIndex(item => item.RecommendationId)
            .HasDatabaseName("ux_recommendation_score_recommendation")
            .IsUnique();
    }

    private static void ConfigureQualityIssue(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<QualityIssueRecord>();

        entity.ToTable("quality_issue");
        ConfigureCommonProperties(entity);
        entity.Property(item => item.HospitalId).HasColumnName("hospital_id").IsRequired();
        entity.Property(item => item.VisitId).HasColumnName("visit_id").IsRequired();
        entity.Property(item => item.CodingTaskId).HasColumnName("coding_task_id").IsRequired();
        entity.Property(item => item.DiagnosisInputId).HasColumnName("diagnosis_input_id");
        entity.Property(item => item.IssueType).HasColumnName("issue_type").HasConversion<int>().IsRequired();
        entity.Property(item => item.RiskLevel).HasColumnName("risk_level").HasMaxLength(32).IsRequired();
        entity.Property(item => item.Description).HasColumnName("description").HasMaxLength(1000).IsRequired();
        entity.Property(item => item.FactId).HasColumnName("fact_id");
        entity.Property(item => item.EvidenceIds).HasColumnName("evidence_ids").HasColumnType("nvarchar(max)");
        entity.Property(item => item.CurrentCode).HasColumnName("current_code").HasMaxLength(64);
        entity.Property(item => item.SuggestedCode).HasColumnName("suggested_code").HasMaxLength(64);
        entity.Property(item => item.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        entity.HasIndex(item => new { item.HospitalId, item.CodingTaskId, item.Status })
            .HasDatabaseName("ix_quality_issue_task_status");
    }

    private static void ConfigureCodingRecommendation(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<CodingRecommendationRecord>();

        entity.ToTable("coding_recommendation");
        ConfigureCommonProperties(entity);
        entity.Property(item => item.HospitalId).HasColumnName("hospital_id").IsRequired();
        entity.Property(item => item.CodingTaskId).HasColumnName("coding_task_id").IsRequired();
        entity.Property(item => item.DiagnosisInputId).HasColumnName("diagnosis_input_id");
        entity.Property(item => item.PipelineVersion).HasColumnName("pipeline_version").HasMaxLength(64).IsRequired();
        entity.Property(item => item.PipelineRunId).HasColumnName("pipeline_run_id");
        entity.Property(item => item.RecommendationVersion).HasColumnName("recommendation_version").HasMaxLength(64);
        entity.Property(item => item.RecommendationType).HasColumnName("recommendation_type").HasMaxLength(64).IsRequired();
        entity.Property(item => item.CodeSystemCode).HasColumnName("code_system_code").HasMaxLength(64).IsRequired();
        entity.Property(item => item.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
        entity.Property(item => item.Title).HasColumnName("title").HasMaxLength(300).IsRequired();
        entity.Property(item => item.Rank).HasColumnName("rank").IsRequired();
        entity.Property(item => item.RecallScore).HasColumnName("recall_score").HasColumnType("decimal(5,4)").IsRequired();
        entity.Property(item => item.RuleScore).HasColumnName("rule_score").HasColumnType("decimal(5,4)").IsRequired();
        entity.Property(item => item.ConfidenceScore).HasColumnName("confidence_score").HasColumnType("decimal(5,4)").IsRequired();
        entity.Property(item => item.ReviewStatus).HasColumnName("review_status").HasMaxLength(32).IsRequired();
        entity.Property(item => item.Outcome).HasColumnName("outcome").HasMaxLength(32).IsRequired();
        entity.Property(item => item.LifecycleStatus).HasColumnName("lifecycle_status").HasMaxLength(32).IsRequired();
        entity.Property(item => item.EvidenceSufficiency).HasColumnName("evidence_sufficiency").HasColumnType("decimal(5,4)");
        entity.Property(item => item.RiskLevel).HasColumnName("risk_level").HasMaxLength(32);
        entity.Property(item => item.Reason).HasColumnName("reason").HasMaxLength(1000);
        entity.Property(item => item.ModelVersion).HasColumnName("model_version").HasMaxLength(128);
        entity.Property(item => item.PromptVersion).HasColumnName("prompt_version").HasMaxLength(128);
        entity.Property(item => item.KnowledgeVersion).HasColumnName("knowledge_version").HasMaxLength(128);
        entity.Property(item => item.RuleVersion).HasColumnName("rule_version").HasMaxLength(128);
        entity.Property(item => item.CodingVersion).HasColumnName("coding_version").HasMaxLength(128);
        entity.Property(item => item.IsReadOnly).HasColumnName("is_read_only").IsRequired();
        // 同一诊断输入重跑后旧推荐记为 STALE 而不是物理删除，因此 URL 唯一性必须按版本维度成立：
        // 旧 MVP 行（recommendation_version 为空）沿用原有 4 列唯一约束，
        // V2.2 行（recommendation_version 递增）在同一次任务下同编码可出现多版本。
        entity.HasIndex(item => new { item.CodingTaskId, item.DiagnosisInputId, item.RecommendationType, item.Code })
            .HasDatabaseName("ux_coding_recommendation_task_input_type_code")
            .IsUnique()
            .HasFilter("[recommendation_version] IS NULL");
        entity.HasIndex(item => new
            {
                item.CodingTaskId,
                item.DiagnosisInputId,
                item.RecommendationType,
                item.Code,
                item.RecommendationVersion
            })
            .HasDatabaseName("ux_coding_recommendation_task_input_type_code_version")
            .IsUnique()
            .HasFilter("[recommendation_version] IS NOT NULL");
        entity.HasIndex(item => new { item.CodingTaskId, item.ReviewStatus })
            .HasDatabaseName("ix_coding_recommendation_task_review_status");
        entity.HasIndex(item => new { item.HospitalId, item.PipelineVersion, item.LifecycleStatus })
            .HasDatabaseName("ix_coding_recommendation_hospital_pipeline_lifecycle");
        entity.HasOne(item => item.Hospital)
            .WithMany()
            .HasForeignKey(item => item.HospitalId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(item => item.CodingTask)
            .WithMany(item => item.CodingRecommendations)
            .HasForeignKey(item => item.CodingTaskId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureRecommendationEvidence(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<RecommendationEvidenceRecord>();

        entity.ToTable("recommendation_evidence");
        ConfigureCommonProperties(entity);
        entity.Property(item => item.HospitalId).HasColumnName("hospital_id").IsRequired();
        entity.Property(item => item.CodingRecommendationId).HasColumnName("coding_recommendation_id").IsRequired();
        entity.Property(item => item.DocumentSectionId).HasColumnName("document_section_id");
        entity.Property(item => item.PipelineRunId).HasColumnName("pipeline_run_id");
        entity.Property(item => item.EvidenceLevel).HasColumnName("evidence_level").HasMaxLength(2);
        entity.Property(item => item.SourceType).HasColumnName("source_type").HasMaxLength(64).IsRequired();
        entity.Property(item => item.SourceText).HasColumnName("source_text").HasMaxLength(1000).IsRequired();
        entity.Property(item => item.MatchText).HasColumnName("match_text").HasMaxLength(300).IsRequired();
        entity.Property(item => item.Score).HasColumnName("score").HasColumnType("decimal(5,4)").IsRequired();
        entity.HasIndex(item => item.CodingRecommendationId)
            .HasDatabaseName("ix_recommendation_evidence_recommendation");
        entity.HasOne(item => item.Hospital)
            .WithMany()
            .HasForeignKey(item => item.HospitalId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(item => item.CodingRecommendation)
            .WithMany(item => item.Evidences)
            .HasForeignKey(item => item.CodingRecommendationId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(item => item.DocumentSection)
            .WithMany()
            .HasForeignKey(item => item.DocumentSectionId)
            .OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureCodingReview(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<CodingReviewRecord>();

        entity.ToTable("coding_review");
        ConfigureCommonProperties(entity);
        entity.Property(item => item.HospitalId).HasColumnName("hospital_id").IsRequired();
        entity.Property(item => item.CodingTaskId).HasColumnName("coding_task_id").IsRequired();
        entity.Property(item => item.ReviewStatus).HasColumnName("review_status").HasMaxLength(32).IsRequired();
        entity.Property(item => item.Comment).HasColumnName("comment").HasMaxLength(1000);
        entity.Property(item => item.ReviewerId).HasColumnName("reviewer_id").HasMaxLength(128);
        // V2.2 审核动作类型：ACCEPTED / REJECTED / MODIFIED。
        entity.Property(item => item.ResultType).HasColumnName("result_type").HasMaxLength(64);
        entity.Property(item => item.ReviewedAt).HasColumnName("reviewed_at").IsRequired();
        entity.HasIndex(item => new { item.CodingTaskId, item.ReviewStatus })
            .HasDatabaseName("ix_coding_review_task_status");
        entity.HasOne(item => item.Hospital)
            .WithMany()
            .HasForeignKey(item => item.HospitalId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(item => item.CodingTask)
            .WithMany()
            .HasForeignKey(item => item.CodingTaskId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureFinalCodingResult(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<FinalCodingResultRecord>();

        entity.ToTable("final_coding_result");
        ConfigureCommonProperties(entity);
        entity.Property(item => item.HospitalId).HasColumnName("hospital_id").IsRequired();
        entity.Property(item => item.CodingTaskId).HasColumnName("coding_task_id").IsRequired();
        entity.Property(item => item.SourceRecommendationId).HasColumnName("source_recommendation_id");
        entity.Property(item => item.ResultType).HasColumnName("result_type").HasMaxLength(64).IsRequired();
        entity.Property(item => item.CodeSystemCode).HasColumnName("code_system_code").HasMaxLength(64).IsRequired();
        entity.Property(item => item.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
        entity.Property(item => item.Title).HasColumnName("title").HasMaxLength(300).IsRequired();
        entity.Property(item => item.ReviewerId).HasColumnName("reviewer_id").HasMaxLength(128);
        entity.Property(item => item.ConfirmedAt).HasColumnName("confirmed_at").IsRequired();
        entity.HasIndex(item => new { item.CodingTaskId, item.ResultType, item.CodeSystemCode, item.Code })
            .HasDatabaseName("ux_final_coding_result_task_type_code")
            .IsUnique();
        entity.HasOne(item => item.Hospital)
            .WithMany()
            .HasForeignKey(item => item.HospitalId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(item => item.CodingTask)
            .WithMany()
            .HasForeignKey(item => item.CodingTaskId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(item => item.SourceRecommendation)
            .WithMany()
            .HasForeignKey(item => item.SourceRecommendationId)
            .OnDelete(DeleteBehavior.SetNull);
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
        entity.Property(item => item.PipelineRunId).HasColumnName("pipeline_run_id");
        entity.Property(item => item.DiagnosisInputId).HasColumnName("diagnosis_input_id");
        entity.Property(item => item.Stage).HasColumnName("stage").HasMaxLength(64).IsRequired();
        entity.Property(item => item.StepName).HasColumnName("step_name").HasMaxLength(128).IsRequired();
        entity.Property(item => item.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        entity.Property(item => item.StartedAt).HasColumnName("started_at").IsRequired();
        entity.Property(item => item.CompletedAt).HasColumnName("completed_at");
        entity.Property(item => item.ErrorCode).HasColumnName("error_code").HasMaxLength(128);
        entity.Property(item => item.DurationMs).HasColumnName("duration_ms");
        entity.Property(item => item.ModelVersion).HasColumnName("model_version").HasMaxLength(128);
        entity.Property(item => item.PromptVersion).HasColumnName("prompt_version").HasMaxLength(128);
        entity.Property(item => item.KnowledgeVersion).HasColumnName("knowledge_version").HasMaxLength(128);
        entity.Property(item => item.RuleVersion).HasColumnName("rule_version").HasMaxLength(128);
        entity.Property(item => item.InputTokens).HasColumnName("input_tokens");
        entity.Property(item => item.OutputTokens).HasColumnName("output_tokens");
        entity.HasIndex(item => new { item.PipelineTraceId, item.StartedAt })
            .HasDatabaseName("ix_pipeline_trace_step_trace_started");
        entity.HasIndex(item => new { item.PipelineRunId, item.Stage })
            .HasDatabaseName("ix_pipeline_trace_step_run_stage");
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
