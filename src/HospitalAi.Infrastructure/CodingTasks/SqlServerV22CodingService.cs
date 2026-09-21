using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HospitalAi.Application.Abstractions;
using HospitalAi.Application.Common;
using HospitalAi.Contracts.CodingRecommendations;
using HospitalAi.Contracts.CodingTasks;
using HospitalAi.Domain.CodingTasks;
using HospitalAi.Infrastructure.SqlServer;
using Microsoft.EntityFrameworkCore;

namespace HospitalAi.Infrastructure.CodingTasks;

/// <summary>
/// V2.2-Lite 编码应用服务。
/// 约定：
/// 1) 所有查询按 RequestContext.HospitalId 隔离，不跨医院访问；
/// 2) 默认只返回 V2.2 PipelineVersion 的当前生效推荐，includeLegacy 才回放 Legacy 结果；
/// 3) Legacy（旧 MVP）推荐只读，不接受修改 / 审核动作；
/// 4) 已确认进入 Final Coding 的推荐不允许被自动重跑覆盖。
/// </summary>
public sealed class SqlServerV22CodingService(
    HospitalAiDbContext dbContext,
    IRequestContext requestContext,
    IV22RecommendationExecutor recommendationExecutor)
    : IV22CodingService
{
    private const string ActiveLifecycleStatus = "ACTIVE";
    private const string LegacyReadOnlyLifecycleStatus = "LEGACY_READ_ONLY";
    private const string StaleLifecycleStatus = "STALE";
    private const string SupersededLifecycleStatus = "SUPERSEDED";
    private const string AcceptedReviewStatus = "ACCEPTED";
    private const string RejectedReviewStatus = "REJECTED";
    private const string PendingReviewStatus = "PENDING_REVIEW";
    private const string ManualSourceSystem = "MANUAL";

    public async Task<VisitReadinessResponse?> GetVisitReadinessAsync(
        Guid visitId,
        CancellationToken cancellationToken = default)
    {
        EnsureHospital();
        var visit = await dbContext.Visits
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == visitId && item.HospitalId == requestContext.HospitalId,
                cancellationToken);
        if (visit is null)
        {
            return null;
        }

        var task = await LoadLatestTaskAsync(visitId, cancellationToken);
        var documents = await LoadCurrentDocumentsAsync(visitId, cancellationToken);
        var readyDocumentCount = documents.Count(item => !string.IsNullOrWhiteSpace(item.ContentReference));
        var diagnosisInputCount = task is null
            ? 0
            : await dbContext.CodingDiagnosisInputs
                .AsNoTracking()
                .CountAsync(
                    item => item.HospitalId == requestContext.HospitalId
                        && item.CodingTaskId == task.Id
                        && item.Status == ActiveLifecycleStatus,
                    cancellationToken);

        var qualityIssues = task is null
            ? []
            : await LoadQualityIssuesAsync(task.Id, cancellationToken);

        var stage = task?.CodingStage ?? CodingStage.Imported;
        var ready = documents.Count > 0
            && readyDocumentCount == documents.Count
            && !qualityIssues.Any(item => item.RiskLevel is "HIGH" or "CRITICAL" && item.Status == "OPEN");
        var degradedFlags = new List<string>();
        if (documents.Count == 0)
        {
            degradedFlags.Add("DEGRADED.NO_DOCUMENT");
        }

        if (readyDocumentCount < documents.Count)
        {
            degradedFlags.Add("DEGRADED.EMPTY_DOCUMENT");
        }

        return new VisitReadinessResponse(
            visit.Id,
            stage.ToWireValue(),
            documents.Count,
            readyDocumentCount,
            diagnosisInputCount,
            task?.Id,
            degradedFlags,
            qualityIssues.Select(ToQualityIssueResponse).ToList());
    }

    public async Task<IReadOnlyList<ClinicalFactResponse>> GetClinicalFactsAsync(
        Guid visitId,
        CancellationToken cancellationToken = default)
    {
        EnsureHospital();
        var task = await LoadLatestTaskAsync(visitId, cancellationToken);
        if (task is null)
        {
            return [];
        }

        var facts = await dbContext.ClinicalFacts
            .AsNoTracking()
            .Where(item => item.HospitalId == requestContext.HospitalId
                && item.CodingTaskId == task.Id
                && item.Status == ActiveLifecycleStatus)
            .OrderBy(item => item.CreatedAt)
            .Select(item => new ClinicalFactResponse(
                item.Id,
                item.FactType.ToWireValue(),
                item.FactName,
                item.NormalizedValue,
                item.OriginalValue,
                item.Negation,
                item.Certainty.ToWireValue(),
                item.Temporality.ToWireValue(),
                item.Confidence,
                item.SourceDocumentId,
                item.SourceSectionId,
                item.SourceStart,
                item.SourceEnd,
                item.ExtractorVersion))
            .ToListAsync(cancellationToken);

        return facts;
    }

    public async Task<IReadOnlyList<VisitDocumentResponse>> GetVisitDocumentsAsync(
        Guid visitId,
        CancellationToken cancellationToken = default)
    {
        EnsureHospital();
        var documents = await dbContext.MedicalDocuments
            .AsNoTracking()
            .Where(item => item.HospitalId == requestContext.HospitalId && item.VisitId == visitId)
            .OrderBy(item => item.CreatedAt)
            .Select(item => new
            {
                item.Id,
                item.DocumentType,
                item.Version,
                item.DocumentStatus,
                item.ParseVersion,
                item.OcrVersion,
                item.ContentHash,
                item.IsCurrent,
                item.CreatedAt
            })
            .ToListAsync(cancellationToken);

        if (documents.Count == 0)
        {
            return [];
        }

        var documentIds = documents.Select(item => item.Id).ToList();
        var chunkCounts = await dbContext.DocumentSections
            .AsNoTracking()
            .Where(item => item.HospitalId == requestContext.HospitalId
                && documentIds.Contains(item.MedicalDocumentId)
                && item.SectionType == "CHUNK")
            .GroupBy(item => item.MedicalDocumentId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var counts = chunkCounts.ToDictionary(item => item.Key, item => item.Count);

        return documents
            .Select(item => new VisitDocumentResponse(
                item.Id,
                item.DocumentType,
                item.Version,
                item.DocumentStatus,
                item.ParseVersion,
                item.OcrVersion,
                item.ContentHash,
                item.IsCurrent,
                counts.TryGetValue(item.Id, out var count) ? count : 0,
                item.CreatedAt))
            .ToList();
    }

    public async Task<VisitCodingResponse?> GetVisitCodingAsync(
        Guid visitId,
        bool includeLegacy = false,
        CancellationToken cancellationToken = default)
    {
        EnsureHospital();
        var visit = await dbContext.Visits
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == visitId && item.HospitalId == requestContext.HospitalId,
                cancellationToken);
        if (visit is null)
        {
            return null;
        }

        var task = await LoadLatestTaskAsync(visitId, cancellationToken);
        var diagnosisInputs = task is null
            ? []
            : await LoadDiagnosisInputsAsync(task.Id, cancellationToken);

        var recommendations = new List<RecommendationDetailResponse>();
        if (task is not null)
        {
            var records = await dbContext.CodingRecommendations
                .AsNoTracking()
                .Where(item => item.HospitalId == requestContext.HospitalId
                    && item.CodingTaskId == task.Id)
                .Where(VisiblePredicate(includeLegacy))
                .OrderBy(item => item.DiagnosisInputId)
                .ThenBy(item => item.Rank)
                .ToListAsync(cancellationToken);

            foreach (var record in records)
            {
                recommendations.Add(await ToDetailAsync(record, cancellationToken));
            }
        }

        var finalResults = new List<FinalCodingResponse>();
        if (task is not null)
        {
            finalResults = await dbContext.FinalCodingResults
                .AsNoTracking()
                .Where(item => item.HospitalId == requestContext.HospitalId && item.CodingTaskId == task.Id)
                .OrderBy(item => item.ConfirmedAt)
                .Select(item => new FinalCodingResponse(
                    item.Id,
                    item.ResultType,
                    item.CodeSystemCode,
                    item.Code,
                    item.Title,
                    item.SourceRecommendationId,
                    item.ReviewerId,
                    item.ConfirmedAt))
                .ToListAsync(cancellationToken);
        }

        return new VisitCodingResponse(
            visit.Id,
            task?.Id,
            (task?.CodingStage ?? CodingStage.Imported).ToWireValue(),
            task?.PipelineVersion,
            diagnosisInputs,
            recommendations,
            finalResults);
    }

    public async Task<IReadOnlyList<DiagnosisInputResponse>> SubmitDiagnosisInputsAsync(
        Guid taskId,
        DiagnosisInputBatchRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureHospital();
        if (request.Items.Count == 0)
        {
            throw new ValidationException("诊断输入不能为空。");
        }

        var task = await dbContext.CodingTasks
            .SingleOrDefaultAsync(
                item => item.Id == taskId && item.HospitalId == requestContext.HospitalId,
                cancellationToken)
            ?? throw new ResourceNotFoundException("编码任务不存在。");

        var now = DateTimeOffset.UtcNow;
        var existing = await dbContext.CodingDiagnosisInputs
            .Where(item => item.HospitalId == requestContext.HospitalId && item.CodingTaskId == task.Id)
            .ToListAsync(cancellationToken);
        var knownText = existing
            .Select(item => item.OriginalText)
            .ToHashSet(StringComparer.Ordinal);

        var created = new List<CodingDiagnosisInputRecord>();
        foreach (var item in request.Items)
        {
            var originalText = (item.OriginalText ?? string.Empty).Trim();
            if (originalText.Length == 0)
            {
                throw new ValidationException("诊断输入原文不能为空。");
            }

            if (!knownText.Add(originalText))
            {
                // 原文重复只跳过，不覆盖既有诊断输入。
                continue;
            }

            var sourceType = string.IsNullOrWhiteSpace(request.SourceType)
                ? "STRUCTURED_INPUT"
                : request.SourceType.Trim().ToUpperInvariant();
            var record = new CodingDiagnosisInputRecord
            {
                Id = Guid.NewGuid(),
                HospitalId = task.HospitalId,
                VisitId = task.VisitId,
                CodingTaskId = task.Id,
                SourceType = sourceType,
                OriginalText = originalText,
                NormalizedText = null,
                IsPrincipal = item.IsPrincipal,
                DiagnosisOrder = item.DiagnosisOrder > 0 ? item.DiagnosisOrder : existing.Count + created.Count + 1,
                SourceDocumentId = item.SourceDocumentId,
                SourceSectionId = item.SourceSectionId,
                Status = ActiveLifecycleStatus,
                CreatedAt = now,
                UpdatedAt = now
            };
            created.Add(record);
        }

        if (created.Count == 0)
        {
            throw new ValidationException("诊断输入原文均与既有输入重复。");
        }

        dbContext.CodingDiagnosisInputs.AddRange(created);
        await dbContext.SaveChangesAsync(cancellationToken);

        return created
            .Select(item => new DiagnosisInputResponse(
                item.Id,
                item.CodingTaskId,
                item.VisitId,
                item.SourceType,
                item.OriginalText,
                item.NormalizedText,
                item.IsPrincipal,
                item.DiagnosisOrder,
                item.Status))
            .ToList();
    }

    public async Task<CaseEntryResponse> CreateCaseEntryAsync(
        CaseEntryRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureHospital();
        var patientName = (request.PatientName ?? string.Empty).Trim();
        var medicalRecordNo = (request.MedicalRecordNo ?? string.Empty).Trim();
        if (patientName.Length == 0)
        {
            throw new ValidationException("患者姓名不能为空。");
        }

        if (medicalRecordNo.Length == 0)
        {
            throw new ValidationException("病案号不能为空。");
        }

        if (request.DischargeAt is { } dischargeAt && dischargeAt < request.AdmissionAt)
        {
            throw new ValidationException("出院时间不能早于入院时间。");
        }

        var admissionDiagnoses = DistinctLines(request.AdmissionDiagnoses);
        var dischargeDiagnoses = DistinctLines(request.DischargeDiagnoses);
        var procedures = DistinctLines(request.Procedures);
        if (dischargeDiagnoses.Count == 0)
        {
            throw new ValidationException("至少录入一条出院诊断。");
        }

        var now = DateTimeOffset.UtcNow;

        // 病案号即 source_patient_id，院内唯一；同案号异姓名按录入错误拦截。
        var patient = await dbContext.Patients
            .SingleOrDefaultAsync(
                item => item.HospitalId == requestContext.HospitalId
                    && item.SourcePatientId == medicalRecordNo,
                cancellationToken);
        if (patient is null)
        {
            patient = new PatientRecord
            {
                Id = Guid.NewGuid(),
                HospitalId = requestContext.HospitalId,
                SourceSystem = ManualSourceSystem,
                SourcePatientId = medicalRecordNo,
                DisplayName = patientName,
                CreatedAt = now,
                UpdatedAt = now
            };
            dbContext.Patients.Add(patient);
        }
        else if (!string.Equals(patient.DisplayName, patientName, StringComparison.Ordinal))
        {
            throw new ValidationException(
                $"病案号 {medicalRecordNo} 已存在（姓名：{patient.DisplayName}），与录入姓名不一致。");
        }

        // 住院次数按该患者既有就诊数推导，与工作台展示口径一致。
        var priorVisitCount = await dbContext.Visits
            .CountAsync(
                item => item.HospitalId == requestContext.HospitalId
                    && item.PatientId == patient.Id,
                cancellationToken);
        var visitSequence = priorVisitCount + 1;
        if (request.AdmissionCount is { } submittedCount && submittedCount != visitSequence)
        {
            throw new ValidationException(
                $"该病案号本次应为第 {visitSequence} 次住院，与录入的住院次数 {submittedCount} 不一致。");
        }

        var visit = new VisitRecord
        {
            Id = Guid.NewGuid(),
            HospitalId = requestContext.HospitalId,
            PatientId = patient.Id,
            AdmissionAt = request.AdmissionAt,
            DischargeAt = request.DischargeAt,
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.Visits.Add(visit);

        var segments = new List<string>();
        if (admissionDiagnoses.Count > 0)
        {
            segments.Add($"【入院诊断】：{string.Join("；", admissionDiagnoses)}。");
        }

        segments.Add($"【出院诊断】：{string.Join("；", dischargeDiagnoses)}。");
        if (procedures.Count > 0)
        {
            segments.Add($"【手术操作】：{string.Join("；", procedures)}。");
        }

        var documentContent = string.Join("\n", segments);
        var document = new MedicalDocumentRecord
        {
            Id = Guid.NewGuid(),
            HospitalId = requestContext.HospitalId,
            VisitId = visit.Id,
            DocumentType = "DISCHARGE_NOTE",
            ContentReference = documentContent,
            ContentHash = Sha256Hex(documentContent),
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.MedicalDocuments.Add(document);

        var additionalContent = (request.AdditionalDocumentContent ?? string.Empty).Trim();
        if (additionalContent.Length > 0)
        {
            dbContext.MedicalDocuments.Add(new MedicalDocumentRecord
            {
                Id = Guid.NewGuid(),
                HospitalId = requestContext.HospitalId,
                VisitId = visit.Id,
                DocumentType = "ADMISSION_NOTE",
                ContentReference = additionalContent,
                ContentHash = Sha256Hex(additionalContent),
                Version = 1,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        var task = new CodingTaskRecord
        {
            Id = Guid.NewGuid(),
            HospitalId = requestContext.HospitalId,
            VisitId = visit.Id,
            PipelineVersion = PipelineVersions.Lite,
            IdempotencyKey = $"case-entry-{Guid.NewGuid():N}",
            Status = CodingTaskStatus.Pending,
            CodingStage = CodingStage.Imported,
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.CodingTasks.Add(task);

        // 出院诊断 → FRONT_PAGE（首条为主诊断）、入院诊断 → ADMISSION_PAGE，
        // 与批量导入口径一致；文书自动条目由流水线按正文解析补齐。
        var inputs = new List<CodingDiagnosisInputRecord>();
        for (var i = 0; i < dischargeDiagnoses.Count; i++)
        {
            inputs.Add(NewCaseEntryInput(
                task, visit, document.Id, "FRONT_PAGE", dischargeDiagnoses[i], i == 0, i + 1, now));
        }

        for (var i = 0; i < admissionDiagnoses.Count; i++)
        {
            inputs.Add(NewCaseEntryInput(
                task, visit, document.Id, "ADMISSION_PAGE", admissionDiagnoses[i], false, i + 1, now));
        }

        dbContext.CodingDiagnosisInputs.AddRange(inputs);
        await dbContext.SaveChangesAsync(cancellationToken);

        var message = new CodingTaskCreatedMessage(
            MessageId: Guid.NewGuid(),
            HospitalId: task.HospitalId,
            TaskId: task.Id,
            VisitId: task.VisitId,
            PipelineVersion: task.PipelineVersion,
            TraceId: requestContext.TraceId ?? Guid.NewGuid().ToString("N"));
        await recommendationExecutor.ExecuteAsync(
            message,
            inputs.Select(item => item.Id).ToList(),
            cancellationToken);

        var recommendationCount = await dbContext.CodingRecommendations
            .CountAsync(
                item => item.HospitalId == requestContext.HospitalId
                    && item.CodingTaskId == task.Id
                    && (item.PipelineVersion == PipelineVersions.Lite || item.PipelineVersion == PipelineVersions.Full)
                    && item.LifecycleStatus == ActiveLifecycleStatus,
                cancellationToken);

        return new CaseEntryResponse(
            patient.Id,
            visit.Id,
            document.Id,
            task.Id,
            task.PipelineVersion,
            visitSequence,
            task.CodingStage.ToWireValue(),
            inputs
                .Select(item => new DiagnosisInputResponse(
                    item.Id,
                    item.CodingTaskId,
                    item.VisitId,
                    item.SourceType,
                    item.OriginalText,
                    item.NormalizedText,
                    item.IsPrincipal,
                    item.DiagnosisOrder,
                    item.Status))
                .ToList(),
            recommendationCount);
    }

    public async Task<RecommendationDetailResponse?> RecommendAsync(
        Guid diagnosisInputId,
        RecommendRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureHospital();
        var input = await dbContext.CodingDiagnosisInputs
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == diagnosisInputId
                    && item.HospitalId == requestContext.HospitalId
                    && item.Status == ActiveLifecycleStatus,
                cancellationToken)
            ?? throw new ResourceNotFoundException("诊断输入不存在。");

        var task = await dbContext.CodingTasks
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == input.CodingTaskId && item.HospitalId == requestContext.HospitalId,
                cancellationToken)
            ?? throw new ResourceNotFoundException("编码任务不存在。");

        // Legacy 任务不允许通过 V2.2 端点触发推荐，避免新旧流水线互相覆盖。
        if (!PipelineVersions.IsV22(task.PipelineVersion))
        {
            throw new ValidationException(
                $"任务 PipelineVersion={task.PipelineVersion} 不是 V2.2 流水线，不能触发 V2.2 推荐。");
        }

        var requestedIds = (request.DiagnosisInputIds ?? [input.Id])
            .Distinct()
            .ToList();
        if (requestedIds.Count > 0 && requestedIds.Any(id => id != input.Id))
        {
            var sameTaskIds = await dbContext.CodingDiagnosisInputs
                .AsNoTracking()
                .Where(item => item.HospitalId == requestContext.HospitalId
                    && item.CodingTaskId == task.Id
                    && requestedIds.Contains(item.Id))
                .Select(item => item.Id)
                .ToListAsync(cancellationToken);
            if (sameTaskIds.Count != requestedIds.Count)
            {
                throw new ValidationException("指定的诊断输入不属于当前编码任务。");
            }
        }

        var message = new CodingTaskCreatedMessage(
            MessageId: Guid.NewGuid(),
            HospitalId: task.HospitalId,
            TaskId: task.Id,
            VisitId: task.VisitId,
            PipelineVersion: task.PipelineVersion,
            TraceId: requestContext.TraceId ?? Guid.NewGuid().ToString("N"));

        await recommendationExecutor.ExecuteAsync(message, requestedIds, cancellationToken);

        var record = await dbContext.CodingRecommendations
            .AsNoTracking()
            .Where(item => item.HospitalId == requestContext.HospitalId
                && item.CodingTaskId == task.Id
                && requestedIds.Contains(input.Id)
                && item.DiagnosisInputId.HasValue
                && (item.PipelineVersion == PipelineVersions.Lite || item.PipelineVersion == PipelineVersions.Full)
                && item.LifecycleStatus == ActiveLifecycleStatus)
            .OrderByDescending(item => item.CreatedAt)
            .ThenBy(item => item.Rank)
            .FirstOrDefaultAsync(cancellationToken);

        return record is null ? null : await ToDetailAsync(record, cancellationToken);
    }

    public async Task<RecommendationListResponse?> GetDiagnosisInputRecommendationsAsync(
        Guid diagnosisInputId,
        bool includeLegacy = false,
        CancellationToken cancellationToken = default)
    {
        EnsureHospital();
        var input = await dbContext.CodingDiagnosisInputs
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == diagnosisInputId && item.HospitalId == requestContext.HospitalId,
                cancellationToken);
        if (input is null)
        {
            return null;
        }

        var records = await dbContext.CodingRecommendations
            .AsNoTracking()
            .Where(item => item.HospitalId == requestContext.HospitalId
                && item.CodingTaskId == input.CodingTaskId
                && item.DiagnosisInputId == input.Id)
            .Where(VisiblePredicate(includeLegacy))
            .OrderBy(item => item.Rank)
            .ToListAsync(cancellationToken);

        var details = new List<RecommendationDetailResponse>();
        foreach (var record in records)
        {
            details.Add(await ToDetailAsync(record, cancellationToken));
        }

        return new RecommendationListResponse(
            input.Id,
            input.CodingTaskId.ToString(),
            details);
    }

    public async Task<RecommendationDetailResponse?> GetRecommendationAsync(
        Guid recommendationId,
        CancellationToken cancellationToken = default)
    {
        EnsureHospital();
        var record = await dbContext.CodingRecommendations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == recommendationId && item.HospitalId == requestContext.HospitalId,
                cancellationToken);
        return record is null ? null : await ToDetailAsync(record, cancellationToken);
    }

    public async Task<ReviewActionResponse?> AcceptRecommendationAsync(
        Guid recommendationId,
        ReviewActionRequest request,
        CancellationToken cancellationToken = default)
    {
        return await ReviewAsync(
            recommendationId,
            AcceptedReviewStatus,
            request,
            cancellationToken);
    }

    public async Task<ReviewActionResponse?> RejectRecommendationAsync(
        Guid recommendationId,
        ReviewActionRequest request,
        CancellationToken cancellationToken = default)
    {
        return await ReviewAsync(
            recommendationId,
            RejectedReviewStatus,
            request,
            cancellationToken);
    }

    public async Task<ReviewActionResponse?> ModifyRecommendationAsync(
        Guid recommendationId,
        ReviewModifyRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureHospital();
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.CodeSystem))
        {
            throw new ValidationException("修改推荐必须同时提供编码与编码体系。");
        }

        var recommendation = await LoadMutableRecommendationAsync(recommendationId, cancellationToken);

        // 修改不覆盖原推荐，另行写入一条 NEED_REVIEW 的候选候选版本，
        // 由编码员确认后作为 Final Coding 的来源。
        var now = DateTimeOffset.UtcNow;
        var modified = new CodingRecommendationRecord
        {
            Id = Guid.NewGuid(),
            HospitalId = recommendation.HospitalId,
            CodingTaskId = recommendation.CodingTaskId,
            DiagnosisInputId = recommendation.DiagnosisInputId,
            PipelineVersion = recommendation.PipelineVersion,
            PipelineRunId = recommendation.PipelineRunId,
            RecommendationVersion = NextVersion(recommendation),
            RecommendationType = recommendation.RecommendationType,
            CodeSystemCode = request.CodeSystem.Trim().ToUpperInvariant(),
            Code = request.Code.Trim(),
            Title = string.IsNullOrWhiteSpace(request.Title) ? recommendation.Title : request.Title.Trim(),
            Rank = recommendation.Rank,
            RecallScore = recommendation.RecallScore,
            RuleScore = recommendation.RuleScore,
            ConfidenceScore = recommendation.ConfidenceScore,
            ReviewStatus = PendingReviewStatus,
            Outcome = nameof(RecommendationOutcome.NeedReview),
            LifecycleStatus = ActiveLifecycleStatus,
            EvidenceSufficiency = recommendation.EvidenceSufficiency,
            RiskLevel = recommendation.RiskLevel,
            Reason = string.IsNullOrWhiteSpace(request.Comment)
                ? $"编码员修改自推荐 {recommendation.Code}"
                : request.Comment,
            ModelVersion = recommendation.ModelVersion,
            PromptVersion = recommendation.PromptVersion,
            KnowledgeVersion = recommendation.KnowledgeVersion,
            RuleVersion = recommendation.RuleVersion,
            CodingVersion = recommendation.CodingVersion,
            IsReadOnly = false,
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.CodingRecommendations.Add(modified);
        await WriteReviewAsync(
            recommendation,
            AcceptedReviewStatus,
            request.Comment ?? "编码员修改推荐编码。",
            request.ReviewerId,
            "MODIFIED",
            now,
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new ReviewActionResponse(
            modified.Id,
            modified.ReviewStatus,
            await CountFinalCodingsAsync(modified.CodingTaskId, cancellationToken));
    }

    public async Task<FinalResultSubmitResponse> SubmitFinalResultsAsync(
        FinalResultSubmitRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureHospital();
        if (request.Items is null || request.Items.Count == 0)
        {
            throw new ValidationException("最终编码不能为空。");
        }

        var codingTaskId = request.CodingTaskId;
        if (codingTaskId.HasValue)
        {
            var task = await dbContext.CodingTasks
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    item => item.Id == codingTaskId.Value && item.HospitalId == requestContext.HospitalId,
                    cancellationToken)
                ?? throw new ResourceNotFoundException("编码任务不存在。");
        }
        else if (request.DiagnosisInputId.HasValue)
        {
            var input = await dbContext.CodingDiagnosisInputs
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    item => item.Id == request.DiagnosisInputId.Value
                        && item.HospitalId == requestContext.HospitalId,
                    cancellationToken)
                ?? throw new ResourceNotFoundException("诊断输入不存在。");
            codingTaskId = input.CodingTaskId;
        }
        else
        {
            throw new ValidationException("必须提供 codingTaskId 或 diagnosisInputId。");
        }

        var recommendationIds = request.Items
            .Where(item => item.SourceRecommendationId.HasValue)
            .Select(item => item.SourceRecommendationId!.Value)
            .Distinct()
            .ToList();
        var sourceRecommendations = recommendationIds.Count == 0
            ? []
            : await dbContext.CodingRecommendations
                .AsNoTracking()
                .Where(item => item.HospitalId == requestContext.HospitalId
                    && recommendationIds.Contains(item.Id))
                .Select(item => new { item.Id, item.CodingTaskId })
                .ToListAsync(cancellationToken);
        if (sourceRecommendations.Count != recommendationIds.Count)
        {
            throw new ValidationException("存在不属于当前医院的来源推荐。");
        }

        if (sourceRecommendations.Any(item => item.CodingTaskId != codingTaskId))
        {
            throw new ValidationException("来源推荐与目标编码任务不一致。");
        }

        // 已确认的 Final Coding 不允许重复提交覆盖：同一来源推荐只能有一次确认结果。
        var existingSources = await dbContext.FinalCodingResults
            .AsNoTracking()
            .Where(item => item.HospitalId == requestContext.HospitalId
                && item.CodingTaskId == codingTaskId.Value
                && item.SourceRecommendationId.HasValue
                && recommendationIds.Contains(item.SourceRecommendationId.Value))
            .Select(item => item.SourceRecommendationId!.Value)
            .ToListAsync(cancellationToken);
        if (existingSources.Count > 0)
        {
            throw new ValidationException(
                $"推荐 {string.Join(",", existingSources.Distinct())} 已确认最终编码，不允许自动覆盖。");
        }

        var now = DateTimeOffset.UtcNow;
        var created = new List<FinalCodingResultRecord>();
        foreach (var item in request.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Code) || string.IsNullOrWhiteSpace(item.CodeSystem))
            {
                throw new ValidationException("最终编码项必须包含编码与编码体系。");
            }

            created.Add(new FinalCodingResultRecord
            {
                Id = Guid.NewGuid(),
                HospitalId = requestContext.HospitalId,
                CodingTaskId = codingTaskId.Value,
                SourceRecommendationId = item.SourceRecommendationId,
                ResultType = string.IsNullOrWhiteSpace(item.ResultType)
                    ? nameof(ClinicalFactType.Diagnosis)
                    : item.ResultType.Trim().ToUpperInvariant(),
                CodeSystemCode = item.CodeSystem.Trim().ToUpperInvariant(),
                Code = item.Code.Trim(),
                Title = item.Title?.Trim() ?? string.Empty,
                ReviewerId = null,
                ConfirmedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        dbContext.FinalCodingResults.AddRange(created);

        var tracked = await dbContext.CodingTasks
            .SingleOrDefaultAsync(
                item => item.Id == codingTaskId.Value && item.HospitalId == requestContext.HospitalId,
                cancellationToken);
        if (tracked is not null)
        {
            tracked.CodingStage = CodingStage.Finalized;
            tracked.Status = CodingTaskStatus.PendingReview;
            tracked.CompletedAt = now;
            tracked.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new FinalResultSubmitResponse(codingTaskId.Value, created.Count, now);
    }

    private async Task<ReviewActionResponse?> ReviewAsync(
        Guid recommendationId,
        string reviewStatus,
        ReviewActionRequest request,
        CancellationToken cancellationToken)
    {
        EnsureHospital();
        var recommendation = await LoadMutableRecommendationAsync(recommendationId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        recommendation.ReviewStatus = reviewStatus;
        recommendation.Outcome = reviewStatus == RejectedReviewStatus
            ? nameof(RecommendationOutcome.NeedReview)
            : recommendation.Outcome;
        recommendation.Reason = string.IsNullOrWhiteSpace(request.Comment)
            ? recommendation.Reason
            : request.Comment;
        recommendation.UpdatedAt = now;

        await WriteReviewAsync(
            recommendation,
            reviewStatus,
            request.Comment ?? string.Empty,
            request.ReviewerId,
            reviewStatus,
            now,
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new ReviewActionResponse(
            recommendation.Id,
            recommendation.ReviewStatus,
            await CountFinalCodingsAsync(recommendation.CodingTaskId, cancellationToken));
    }

    private async Task<CodingRecommendationRecord> LoadMutableRecommendationAsync(
        Guid recommendationId,
        CancellationToken cancellationToken)
    {
        var recommendation = await dbContext.CodingRecommendations
            .SingleOrDefaultAsync(
                item => item.Id == recommendationId && item.HospitalId == requestContext.HospitalId,
                cancellationToken)
            ?? throw new ResourceNotFoundException("推荐不存在。");

        // Legacy 推荐只读，不接受 V2.2 审核动作。
        if (recommendation.IsReadOnly
            || string.Equals(recommendation.LifecycleStatus, LegacyReadOnlyLifecycleStatus, StringComparison.Ordinal)
            || string.Equals(recommendation.PipelineVersion, PipelineVersions.Legacy, StringComparison.Ordinal))
        {
            throw new ValidationException("Legacy 推荐为只读结果，不能修改。");
        }

        return recommendation;
    }

    private async Task WriteReviewAsync(
        CodingRecommendationRecord recommendation,
        string reviewStatus,
        string comment,
        string? reviewerId,
        string resultType,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.CodingReviews
            .Where(item => item.HospitalId == recommendation.HospitalId
                && item.CodingTaskId == recommendation.CodingTaskId
                && item.ReviewStatus == reviewStatus)
            .OrderByDescending(item => item.ReviewedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is not null
            && existing.ReviewerId == reviewerId
            && string.Equals(existing.Comment, comment, StringComparison.Ordinal))
        {
            return;
        }

        dbContext.CodingReviews.Add(new CodingReviewRecord
        {
            Id = Guid.NewGuid(),
            HospitalId = recommendation.HospitalId,
            CodingTaskId = recommendation.CodingTaskId,
            ReviewStatus = reviewStatus,
            Comment = comment,
            ReviewerId = reviewerId,
            ReviewedAt = now,
            ResultType = resultType,
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    private async Task<int> CountFinalCodingsAsync(
        Guid codingTaskId,
        CancellationToken cancellationToken)
    {
        return await dbContext.FinalCodingResults
            .AsNoTracking()
            .CountAsync(
                item => item.HospitalId == requestContext.HospitalId && item.CodingTaskId == codingTaskId,
                cancellationToken);
    }

    private static string NextVersion(CodingRecommendationRecord recommendation)
    {
        var current = recommendation.RecommendationVersion ?? "r-1";
        var separator = current.LastIndexOf('-');
        return separator >= 0 && int.TryParse(current.AsSpan(separator + 1), out var index)
            ? $"r-{index + 1}"
            : "r-2";
    }

    /// <summary>
    /// Legacy / STALE / SUPERSEDED 可见性判定（SQL 可翻译版本）。
    /// 语义与原 IsVisible 一致：
    /// - Legacy（只读 / phase2-mvp-legacy / LEGACY_READ_ONLY）仅在 includeLegacy=true 时可见；
    /// - 非 Legacy 的 STALE / SUPERSEDED 属于历史回放，默认与 includeLegacy 均不返回。
    /// </summary>
    private static System.Linq.Expressions.Expression<Func<CodingRecommendationRecord, bool>> VisiblePredicate(
        bool includeLegacy)
    {
        return item => includeLegacy
            ? item.IsReadOnly
                || item.PipelineVersion == PipelineVersions.Legacy
                || item.LifecycleStatus == LegacyReadOnlyLifecycleStatus
                || (item.LifecycleStatus != StaleLifecycleStatus
                    && item.LifecycleStatus != SupersededLifecycleStatus)
            : !item.IsReadOnly
                && item.PipelineVersion != PipelineVersions.Legacy
                && item.LifecycleStatus != LegacyReadOnlyLifecycleStatus
                && item.LifecycleStatus != StaleLifecycleStatus
                && item.LifecycleStatus != SupersededLifecycleStatus;
    }

    private async Task<CodingTaskRecord?> LoadLatestTaskAsync(
        Guid visitId,
        CancellationToken cancellationToken)
    {
        return await dbContext.CodingTasks
            .AsNoTracking()
            .Where(item => item.HospitalId == requestContext.HospitalId && item.VisitId == visitId)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<List<MedicalDocumentRecord>> LoadCurrentDocumentsAsync(
        Guid visitId,
        CancellationToken cancellationToken)
    {
        return await dbContext.MedicalDocuments
            .AsNoTracking()
            .Where(item => item.HospitalId == requestContext.HospitalId
                && item.VisitId == visitId
                && item.IsCurrent
                && item.DocumentStatus == "ACTIVE")
            .ToListAsync(cancellationToken);
    }

    private async Task<List<DiagnosisInputResponse>> LoadDiagnosisInputsAsync(
        Guid codingTaskId,
        CancellationToken cancellationToken)
    {
        return await dbContext.CodingDiagnosisInputs
            .AsNoTracking()
            .Where(item => item.HospitalId == requestContext.HospitalId
                && item.CodingTaskId == codingTaskId
                && item.Status == ActiveLifecycleStatus)
            .OrderBy(item => item.DiagnosisOrder)
            .Select(item => new DiagnosisInputResponse(
                item.Id,
                item.CodingTaskId,
                item.VisitId,
                item.SourceType,
                item.OriginalText,
                item.NormalizedText,
                item.IsPrincipal,
                item.DiagnosisOrder,
                item.Status))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<QualityIssueRecord>> LoadQualityIssuesAsync(
        Guid codingTaskId,
        CancellationToken cancellationToken)
    {
        return await dbContext.QualityIssues
            .AsNoTracking()
            .Where(item => item.HospitalId == requestContext.HospitalId
                && item.CodingTaskId == codingTaskId
                && item.Status == "OPEN")
            .OrderByDescending(item => item.RiskLevel)
            .ThenBy(item => item.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    private async Task<RecommendationDetailResponse> ToDetailAsync(
        CodingRecommendationRecord record,
        CancellationToken cancellationToken)
    {
        var score = await dbContext.RecommendationScores
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.RecommendationId == record.Id, cancellationToken);

        var candidates = await dbContext.CodingCandidates
            .AsNoTracking()
            .Where(item => item.CodingTaskId == record.CodingTaskId
                && item.DiagnosisInputId == record.DiagnosisInputId
                && item.PipelineRunId == record.PipelineRunId)
            .OrderBy(item => item.Rank)
            .Select(item => new CodingCandidateResponse(
                item.Id,
                item.CodeSystem,
                item.Code,
                item.Title,
                item.RecallSource,
                item.ExactScore,
                item.Bm25Score,
                item.VectorScore,
                item.RerankScore,
                item.RuleScore,
                item.EvidenceScore,
                item.FinalScore,
                item.Rank))
            .ToListAsync(cancellationToken);

        var evidences = await dbContext.RecommendationEvidences
            .AsNoTracking()
            .Where(item => item.HospitalId == requestContext.HospitalId
                && item.CodingRecommendationId == record.Id)
            .OrderByDescending(item => item.Score)
            .Select(item => new RecommendationEvidenceResponse(
                item.SourceType,
                item.SourceText,
                item.MatchText,
                item.Score,
                item.EvidenceLevel,
                item.PipelineRunId,
                item.DocumentSectionId))
            .ToListAsync(cancellationToken);

        var qualityIssues = await dbContext.QualityIssues
            .AsNoTracking()
            .Where(item => item.HospitalId == requestContext.HospitalId
                && item.CodingTaskId == record.CodingTaskId
                && (item.DiagnosisInputId == record.DiagnosisInputId
                    || item.DiagnosisInputId == null)
                && item.Status == "OPEN")
            .OrderByDescending(item => item.RiskLevel)
            .ToListAsync(cancellationToken);

        var degradedFlags = ReadDegradedFlags(record.PipelineRunId);

        return new RecommendationDetailResponse(
            record.Id,
            record.CodingTaskId,
            record.DiagnosisInputId,
            record.RecommendationType,
            record.CodeSystemCode,
            record.Code,
            record.Title,
            record.Rank,
            record.ConfidenceScore,
            record.ConfidenceScore,
            record.Outcome,
            record.LifecycleStatus,
            record.EvidenceSufficiency,
            record.RiskLevel,
            record.Reason,
            record.ReviewStatus,
            record.PipelineVersion,
            record.PipelineRunId,
            record.RecommendationVersion,
            record.KnowledgeVersion,
            record.RuleVersion,
            record.CodingVersion,
            record.ModelVersion,
            degradedFlags,
            record.IsReadOnly,
            score is null
                ? null
                : new RecommendationScoreResponse(
                    score.ExactScore,
                    score.SemanticScore,
                    score.RetrievalScore,
                    score.RerankScore,
                    score.RuleScore,
                    score.EvidenceScore,
                    score.LlmScore,
                    score.MarginScore,
                    score.ScoreProfile),
            candidates,
            evidences.Select(item => new RecommendationEvidenceResponse(
                item.SourceType,
                item.SourceText,
                item.MatchText,
                item.Score,
                item.EvidenceLevel,
                item.PipelineRunId,
                item.DocumentSectionId)).ToList(),
            qualityIssues.Select(ToQualityIssueResponse).ToList());
    }

    private List<string> ReadDegradedFlags(Guid? pipelineRunId)
    {
        if (pipelineRunId is not { } runId)
        {
            return [];
        }

        var flags = dbContext.PipelineTraceSteps
            .AsNoTracking()
            .Where(item => item.PipelineRunId == runId
                && item.Status == "DEGRADED"
                && item.ErrorCode != null)
            .Select(item => item.ErrorCode!)
            .ToList();

        return flags
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static QualityIssueResponse ToQualityIssueResponse(QualityIssueRecord item)
    {
        var evidenceIds = ReadEvidenceIds(item.EvidenceIds);
        return new QualityIssueResponse(
            item.Id,
            item.IssueType.ToWireValue(),
            item.RiskLevel,
            item.Description,
            item.FactId,
            evidenceIds,
            item.CurrentCode,
            item.SuggestedCode,
            item.Status);
    }

    private static IReadOnlyList<Guid> ReadEvidenceIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<Guid>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void EnsureHospital()
    {
        if (requestContext.HospitalId == Guid.Empty)
        {
            throw new ValidationException("缺少医院上下文，拒绝跨医院访问。");
        }
    }

    private static CodingDiagnosisInputRecord NewCaseEntryInput(
        CodingTaskRecord task,
        VisitRecord visit,
        Guid documentId,
        string sourceType,
        string originalText,
        bool isPrincipal,
        int diagnosisOrder,
        DateTimeOffset now)
    {
        return new CodingDiagnosisInputRecord
        {
            Id = Guid.NewGuid(),
            HospitalId = task.HospitalId,
            VisitId = visit.Id,
            CodingTaskId = task.Id,
            SourceType = sourceType,
            OriginalText = originalText,
            NormalizedText = null,
            IsPrincipal = isPrincipal,
            DiagnosisOrder = diagnosisOrder,
            SourceDocumentId = documentId,
            SourceSectionId = null,
            Status = ActiveLifecycleStatus,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static List<string> DistinctLines(IReadOnlyList<string>? lines)
    {
        return (lines ?? [])
            .Select(item => (item ?? string.Empty).Trim())
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static string Sha256Hex(string content)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
    }
}
