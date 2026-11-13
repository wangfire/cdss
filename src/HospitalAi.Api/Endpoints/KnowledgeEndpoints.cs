using HospitalAi.Api;
using HospitalAi.Domain.CodingTasks;
using HospitalAi.Worker.Pipeline;
using HospitalAi.Infrastructure.Outbox;
using HospitalAi.Api.Observability;
using HospitalAi.Api.Security;
using HospitalAi.Application.Abstractions;
using HospitalAi.Application.Common;
using HospitalAi.Application.Coding.NameLookup;
using HospitalAi.Application.Security;
using HospitalAi.Contracts.CodingTasks;
using HospitalAi.Contracts.CodingKnowledge;
using HospitalAi.Contracts.CodingRecommendations;
using HospitalAi.Contracts.Documents;
using HospitalAi.Contracts.Hospitals;
using HospitalAi.Contracts.Patients;
using HospitalAi.Contracts.Security;
using HospitalAi.Contracts.Visits;
using HospitalAi.Infrastructure.CodingKnowledge;
using HospitalAi.Infrastructure.CodingLookup;
using HospitalAi.Infrastructure.CodingRecommendations;
using HospitalAi.Infrastructure.CodingTasks;
using HospitalAi.Infrastructure.Observability;
using HospitalAi.Infrastructure.ReferenceData;
using HospitalAi.Infrastructure.RabbitMq;
using HospitalAi.Infrastructure.Security;
using HospitalAi.Infrastructure.SqlServer;
using HospitalAi.Infrastructure.Tracing;
using HospitalAi.Worker;
using OpenTelemetry.Exporter.Prometheus;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;
using System.Threading.RateLimiting;

namespace HospitalAi.Api.Endpoints;

/// <summary>
/// Knowledge 相关端点。由 Program.cs 拆分而来（纯代码搬移，行为不变）。
/// </summary>
public static class KnowledgeEndpoints
{
    /// <summary>知识库字典/索引/同义词端点（由 Program.cs 搬移）。</summary>
    /// <remarks>参数类型为 WebApplication：启停端点的 invalidate 闭包需要 app.Services。</remarks>
    public static WebApplication MapKnowledgeEndpoints(this WebApplication app)
    {
    app.MapPost(
        "/api/v1/code-systems/import",
        async (
            ImportCodeSystemRequest request,
            ICodingKnowledgeService service,
            HospitalAi.Application.Coding.IcdIndex.IIcdDictionarySource dictionarySource,
            CancellationToken cancellationToken) =>
        {
            var response = await service.ImportCodeSystemAsync(request, cancellationToken);
            dictionarySource.Invalidate();
            return Results.Ok(ApiEnvelope.Create(response));
        }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.KnowledgeCodeSystemImport));

    app.MapPost(
        "/api/v1/coding-rules/import",
        async (
            ImportCodingRulesRequest request,
            ICodingKnowledgeService service,
            HospitalAi.Application.Coding.Matching.IIcdTermSynonymSource synonymSource,
            CancellationToken cancellationToken) =>
        {
            var response = await service.ImportCodingRulesAsync(request, cancellationToken);
            // 同义词同时写入 icd_term_synonym（索引链路词库），导入后立即生效。
            synonymSource.Invalidate();
            return Results.Ok(ApiEnvelope.Create(response));
        }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.KnowledgeRulesImport));

    // 查询知识库数据
    // 注意：code_system / medical_code / term_synonym / coding_rule 四张表都带 hospital_id，
    // 查询必须按当前请求的医院过滤 —— 否则医院 A 的编码员能读到医院 B 的字典与规则（跨院越权）。
    // icd_index / cm3_index / tumor_index 三张索引表没有 hospital_id（平台共享），不受此约束。
    app.MapGet(
        "/api/v1/knowledge/code-systems",
        async (
            HospitalAiDbContext db,
            IRequestContext requestContext,
            CancellationToken cancellationToken) =>
        {
            var systems = await db.CodeSystems
                .Where(s => s.HospitalId == requestContext.HospitalId)
                .OrderByDescending(s => s.CreatedAt)
                .Select(s => new { s.Id, s.Code, s.Name, s.Version, s.CreatedAt })
                .ToListAsync(cancellationToken);
            return Results.Ok(ApiEnvelope.Create(systems));
        }).RequireAuthorization(RbacPolicies.KnowledgeReader);

    app.MapGet(
        "/api/v1/knowledge/medical-codes",
        async (
            HospitalAiDbContext db,
            IRequestContext requestContext,
            int? page,
            int? pageSize,
            string? codeSystem,
            string? keyword,
            CancellationToken cancellationToken) =>
        {
            if (requestContext.HospitalId == Guid.Empty)
            {
                return Results.BadRequest(ApiEnvelope.Create<object>(new { error = "HOSPITAL_CONTEXT_REQUIRED" }));
            }

            // 医院隔离：只返回当前医院的字典行。
            // 字典展示去重：多医院各导入一份相同字典副本时，同 code_system+code+title 存在多行
            // （实测 102100 行 = 51050 条 × 2 院），弹窗与知识库页面只应展示一条。
            // 保留每组最小 Id 行；同一编码不同标题（差异副本）仍各自展示。
            // 运行时字典快照按医院隔离加载（EfIcdDictionarySource），不受此展示层去重影响。
            var dedupedIds = db.MedicalCodes
                .Where(code => code.HospitalId == requestContext.HospitalId)
                .GroupBy(code => new { code.CodeSystemCode, code.Code, code.Title })
                .Select(group => (Guid?)group.Min(code => code.Id));
            IQueryable<MedicalCodeRecord> query = db.MedicalCodes
                .Where(code => dedupedIds.Contains(code.Id));
            if (!string.IsNullOrEmpty(codeSystem))
                query = query.Where(c => c.CodeSystemCode == codeSystem);
            if (!string.IsNullOrEmpty(keyword))
                query = query.Where(c => c.Code.Contains(keyword) || c.Title.Contains(keyword) || c.SearchText.Contains(keyword));

            var total = await query.CountAsync(cancellationToken);
            var pageNum = page ?? 1;
            var size = pageSize ?? 20;
            var items = await query
                .OrderBy(c => c.Code)
                .Skip((pageNum - 1) * size)
                .Take(size)
                .Select(c => new { c.Id, c.Code, c.Title, c.CodeSystemCode, c.SearchText, c.IsEnabled })
                .ToListAsync(cancellationToken);

            return Results.Ok(ApiEnvelope.Create(new { total, page = pageNum, pageSize = size, items }));
        }).RequireAuthorization(RbacPolicies.KnowledgeReader);

    app.MapGet(
        "/api/v1/knowledge/term-synonyms",
        async (
            HospitalAiDbContext db,
            IRequestContext requestContext,
            int? page,
            int? pageSize,
            string? keyword,
            CancellationToken cancellationToken) =>
        {
            if (requestContext.HospitalId == Guid.Empty)
            {
                return Results.BadRequest(ApiEnvelope.Create<object>(new { error = "HOSPITAL_CONTEXT_REQUIRED" }));
            }

            var query = db.TermSynonyms
                .Where(s => s.HospitalId == requestContext.HospitalId);
            if (!string.IsNullOrEmpty(keyword))
                query = query.Where(s => s.Term.Contains(keyword) || s.NormalizedTerm.Contains(keyword));

            var total = await query.CountAsync(cancellationToken);
            var pageNum = page ?? 1;
            var size = pageSize ?? 20;
            var items = await query
                .OrderBy(s => s.Term)
                .Skip((pageNum - 1) * size)
                .Take(size)
                .Select(s => new { s.Id, s.Term, s.NormalizedTerm, s.EntityType, s.CodeSystemCode, s.Code })
                .ToListAsync(cancellationToken);

            return Results.Ok(ApiEnvelope.Create(new { total, page = pageNum, pageSize = size, items }));
        }).RequireAuthorization(RbacPolicies.KnowledgeReader);

    app.MapGet(
        "/api/v1/knowledge/coding-rules",
        async (
            HospitalAiDbContext db,
            IRequestContext requestContext,
            int? page,
            int? pageSize,
            string? codeSystem,
            CancellationToken cancellationToken) =>
        {
            if (requestContext.HospitalId == Guid.Empty)
            {
                return Results.BadRequest(ApiEnvelope.Create<object>(new { error = "HOSPITAL_CONTEXT_REQUIRED" }));
            }

            var query = db.CodingRules
                .Where(r => r.HospitalId == requestContext.HospitalId);
            if (!string.IsNullOrEmpty(codeSystem))
                query = query.Where(r => r.CodeSystemCode == codeSystem);

            var total = await query.CountAsync(cancellationToken);
            var pageNum = page ?? 1;
            var size = pageSize ?? 20;
            var items = await query
                .OrderBy(r => r.RuleCode)
                .Skip((pageNum - 1) * size)
                .Take(size)
                .Select(r => new { r.Id, r.RuleCode, CodeSystem = r.CodeSystemCode, r.CodePattern, r.RuleType, r.Severity, r.Message, r.IsEnabled })
                .ToListAsync(cancellationToken);

            return Results.Ok(ApiEnvelope.Create(new { total, page = pageNum, pageSize = size, items }));
        }).RequireAuthorization(RbacPolicies.KnowledgeReader);

    // ICD 索引库导入（按序号 upsert，全列保留）。
    app.MapPost(
        "/api/v1/icd-index/import",
        async (
            ImportIcdIndexRequest request,
            HospitalAiDbContext db,
            HospitalAi.Application.Coding.IcdIndex.IIcdIndexSource indexSource,
            CancellationToken cancellationToken) =>
        {
            var now = DateTimeOffset.UtcNow;
            var seqNos = request.Items.Select(item => item.SeqNo).ToArray();
            var existing = await db.IcdIndexes
                .Where(item => seqNos.Contains(item.SeqNo))
                .ToDictionaryAsync(item => item.SeqNo, cancellationToken);

            var imported = 0;
            var updated = 0;
            foreach (var item in request.Items)
            {
                if (existing.TryGetValue(item.SeqNo, out var record))
                {
                    record.RawName = item.RawName;
                    record.RawNameEn = item.RawNameEn;
                    record.IndexName = item.IndexName;
                    record.IndexCode = item.IndexCode;
                    record.IndexLevel = item.IndexLevel;
                    record.ParentSeqNo = item.ParentSeqNo;
                    record.FullIndexName = item.FullIndexName;
                    record.LeadWordStatus = item.LeadWordStatus;
                    record.UpdatedAt = now;
                    updated++;
                }
                else
                {
                    db.IcdIndexes.Add(new IcdIndexRecord
                    {
                        Id = Guid.NewGuid(),
                        SeqNo = item.SeqNo,
                        RawName = item.RawName,
                        RawNameEn = item.RawNameEn,
                        IndexName = item.IndexName,
                        IndexCode = item.IndexCode,
                        IndexLevel = item.IndexLevel,
                        ParentSeqNo = item.ParentSeqNo,
                        FullIndexName = item.FullIndexName,
                        LeadWordStatus = item.LeadWordStatus,
                        CreatedAt = now,
                        UpdatedAt = now,
                    });
                    imported++;
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            indexSource.Invalidate();
            return Results.Ok(ApiEnvelope.Create(
                new ImportIcdIndexResponse(imported, updated)));
        }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.KnowledgeIcdImport));

    // 查询 ICD 索引库（leadWordStatus：0=有效，1=无效，缺省返回全部）。
    app.MapGet(
        "/api/v1/knowledge/icd-index",
        async (
            HospitalAiDbContext db,
            int? page,
            int? pageSize,
            string? keyword,
            int? leadWordStatus,
            int? indexLevel,
            CancellationToken cancellationToken) =>
        {
            var query = db.IcdIndexes.AsQueryable();
            if (!string.IsNullOrEmpty(keyword))
            {
                query = query.Where(item =>
                    item.RawName.Contains(keyword)
                    || item.IndexName.Contains(keyword)
                    || item.FullIndexName.Contains(keyword)
                    || (item.IndexCode != null && item.IndexCode.Contains(keyword))
                    || (item.RawNameEn != null && item.RawNameEn.Contains(keyword)));
            }
            if (leadWordStatus is int status)
                query = query.Where(item => item.LeadWordStatus == status);
            if (indexLevel is int level)
                query = query.Where(item => item.IndexLevel == level);

            var total = await query.CountAsync(cancellationToken);
            var pageNum = page ?? 1;
            var size = Math.Min(pageSize ?? 20, 200);
            var items = await query
                .OrderBy(item => item.SeqNo)
                .Skip((pageNum - 1) * size)
                .Take(size)
                .Select(item => new
                {
                    item.Id,
                    item.SeqNo,
                    item.RawName,
                    item.RawNameEn,
                    item.IndexName,
                    item.IndexCode,
                    item.IndexLevel,
                    item.ParentSeqNo,
                    item.FullIndexName,
                    item.LeadWordStatus,
                    item.IsEnabled,
                })
                .ToListAsync(cancellationToken);

            return Results.Ok(ApiEnvelope.Create(new { total, page = pageNum, pageSize = size, items }));
        }).RequireAuthorization(RbacPolicies.KnowledgeReader);

    // ICD 索引库节点树：按父序号取直接子节点，并标记 each 节点是否还有下一级。
    app.MapGet(
        "/api/v1/knowledge/icd-index/children",
        async (
            HospitalAiDbContext db,
            int? parentSeqNo,
            int? leadWordStatus,
            int? offset,
            int? limit,
            CancellationToken cancellationToken) =>
        {
            var parentId = parentSeqNo ?? 0;
            var take = Math.Min(limit ?? 500, 1000);
            var query = db.IcdIndexes.Where(item => item.ParentSeqNo == parentId);
            if (leadWordStatus is int status)
                query = query.Where(item => item.LeadWordStatus == status);

            var total = await query.CountAsync(cancellationToken);
            var children = await query
                .OrderBy(item => item.SeqNo)
                .Skip(offset ?? 0)
                .Take(take)
                .Select(item => new
                {
                    item.Id,
                    item.SeqNo,
                    item.RawName,
                    item.RawNameEn,
                    item.IndexName,
                    item.IndexCode,
                    item.IndexLevel,
                    item.ParentSeqNo,
                    item.FullIndexName,
                    item.LeadWordStatus,
                    item.IsEnabled,
                })
                .ToListAsync(cancellationToken);

            var seqNos = children.Select(item => item.SeqNo).ToArray();
            var parentsWithChildren = await db.IcdIndexes
                .Where(item => seqNos.Contains(item.ParentSeqNo))
                .Select(item => item.ParentSeqNo)
                .Distinct()
                .ToListAsync(cancellationToken);
            var parentSet = parentsWithChildren.ToHashSet();

            var items = children.Select(item => new
            {
                item.Id,
                item.SeqNo,
                item.RawName,
                item.RawNameEn,
                item.IndexName,
                item.IndexCode,
                item.IndexLevel,
                item.ParentSeqNo,
                item.FullIndexName,
                item.LeadWordStatus,
                item.IsEnabled,
                hasChildren = parentSet.Contains(item.SeqNo),
            });

            return Results.Ok(ApiEnvelope.Create(new { parentSeqNo = parentId, total, items }));
        }).RequireAuthorization(RbacPolicies.KnowledgeReader);

    // ICD 索引库单行新增：seqNo 全库唯一，父节点必须已存在；新增后失效快照让下钻用上新树。
    app.MapPost(
        "/api/v1/knowledge/icd-index",
        async (
            IndexEntryInput input,
            HospitalAiDbContext db,
            HospitalAi.Application.Coding.IcdIndex.IIcdIndexSource indexSource,
            CancellationToken cancellationToken) =>
        {
            var seqNo = input.SeqNo ?? 0;
            var indexName = (input.IndexName ?? string.Empty).Trim();
            var parentSeqNo = input.ParentSeqNo ?? 0;
            var indexLevel = input.IndexLevel ?? 0;
            var leadWordStatus = input.LeadWordStatus ?? 0;
            if (seqNo <= 0)
                throw new ValidationException("序号必须是正整数。");
            if (indexName.Length == 0 || indexName.Length > 200)
                throw new ValidationException("索引名称不能为空且不能超过 200 字。");
            if (parentSeqNo < 0)
                throw new ValidationException("父节点序号不能为负数。");
            if (indexLevel is < 0 or > 9)
                throw new ValidationException("索引级别只能是 0~9。");
            if (leadWordStatus is not (0 or 1))
                throw new ValidationException("主导词状态只能是 0（有效）或 1（无效）。");
            if (parentSeqNo > 0
                && !await db.IcdIndexes.AnyAsync(item => item.SeqNo == parentSeqNo, cancellationToken))
            {
                throw new ValidationException($"父节点序号 {parentSeqNo} 不存在。");
            }

            if (await db.IcdIndexes.AnyAsync(item => item.SeqNo == seqNo, cancellationToken))
                throw new ValidationException($"序号 {seqNo} 已存在，请换一个序号。");

            var now = DateTimeOffset.UtcNow;
            var rawName = (input.RawName ?? string.Empty).Trim();
            var record = new IcdIndexRecord
            {
                Id = Guid.NewGuid(),
                SeqNo = seqNo,
                RawName = rawName.Length > 0 ? rawName : indexName,
                RawNameEn = IndexEditHelpers.TrimToNull(input.RawNameEn),
                IndexName = indexName,
                IndexCode = IndexEditHelpers.TrimToNull(input.IndexCode),
                IndexLevel = indexLevel,
                ParentSeqNo = parentSeqNo,
                FullIndexName = IndexEditHelpers.TrimToNull(input.FullIndexName) ?? indexName,
                LeadWordStatus = leadWordStatus,
                CreatedAt = now,
                UpdatedAt = now,
            };
            if (record.FullIndexName.Length > 300)
                throw new ValidationException("完整索引名称不能超过 300 字。");
            db.IcdIndexes.Add(record);
            await db.SaveChangesAsync(cancellationToken);
            indexSource.Invalidate();
            return Results.Ok(ApiEnvelope.Create(new { id = record.Id, seqNo = record.SeqNo }));
        }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.KnowledgeIcdCreate));

    // ICD 索引库单行修改：序号不可改（父链按 seqNo 引用），其余字段为空表示沿用原值。
    app.MapPut(
        "/api/v1/knowledge/icd-index/{id:guid}",
        async (
            Guid id,
            IndexEntryInput input,
            HospitalAiDbContext db,
            HospitalAi.Application.Coding.IcdIndex.IIcdIndexSource indexSource,
            CancellationToken cancellationToken) =>
        {
            var record = await db.IcdIndexes.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (record is null)
                return Results.NotFound(ApiEnvelope.Create(new { message = "索引行不存在或已被删除。" }));

            var indexName = IndexEditHelpers.TrimToNull(input.IndexName) ?? record.IndexName;
            if (indexName.Length > 200)
                throw new ValidationException("索引名称不能超过 200 字。");
            var parentSeqNo = input.ParentSeqNo ?? record.ParentSeqNo;
            if (parentSeqNo < 0)
                throw new ValidationException("父节点序号不能为负数。");
            if (parentSeqNo > 0 && parentSeqNo != record.ParentSeqNo
                && !await db.IcdIndexes.AnyAsync(item => item.SeqNo == parentSeqNo, cancellationToken))
            {
                throw new ValidationException($"父节点序号 {parentSeqNo} 不存在。");
            }
            if (input.IndexLevel is int level && level is < 0 or > 9)
                throw new ValidationException("索引级别只能是 0~9。");
            if (input.LeadWordStatus is int status && status is not (0 or 1))
                throw new ValidationException("主导词状态只能是 0（有效）或 1（无效）。");

            await IndexEditHelpers.EnsureNoIndexParentCycleAsync(
                record.SeqNo,
                parentSeqNo,
                seq => db.IcdIndexes.Where(item => item.SeqNo == seq)
                    .Select(item => (int?)item.ParentSeqNo).FirstOrDefaultAsync(cancellationToken),
                cancellationToken);

            record.RawName = IndexEditHelpers.TrimToNull(input.RawName) ?? record.RawName;
            record.RawNameEn = input.RawNameEn is null ? record.RawNameEn : IndexEditHelpers.TrimToNull(input.RawNameEn);
            record.IndexName = indexName;
            record.IndexCode = input.IndexCode is null ? record.IndexCode : IndexEditHelpers.TrimToNull(input.IndexCode);
            record.IndexLevel = input.IndexLevel ?? record.IndexLevel;
            record.ParentSeqNo = parentSeqNo;
            record.FullIndexName = IndexEditHelpers.TrimToNull(input.FullIndexName) ?? record.FullIndexName;
            if (record.FullIndexName.Length > 300)
                throw new ValidationException("完整索引名称不能超过 300 字。");
            record.LeadWordStatus = input.LeadWordStatus ?? record.LeadWordStatus;
            record.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            indexSource.Invalidate();
            return Results.Ok(ApiEnvelope.Create(new { id = record.Id, seqNo = record.SeqNo }));
        }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.KnowledgeIcdUpdate));

    // 索引行停用/启用（WP-C 知识库治理）：错行停用而非删除，可回滚；停用动作写审计。
    // 权限按索引类型分别判定：icd/cm3/tumor 各有自己的 update 权限码，
    // 不能一律用 icd.update——否则只授 cm3/tumor 权限的角色改不动对应索引，
    // 而只授 icd.update 的角色却能改 cm3/tumor（越权）。
    app.MapPatch(
        "/api/v1/knowledge/{kind}-index/{id:guid}/enabled",
        async (
            string kind,
            Guid id,
            UpdateIndexEnabledRequest request,
            HospitalAiDbContext db,
            IRequestContext requestContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            // 每种索引的停用/启用对应其「更新」权限码（与前端按钮判定一致）。
            var requiredPermission = kind switch
            {
                "icd" => AppPermissions.KnowledgeIcdUpdate,
                "cm3" => AppPermissions.KnowledgeCm3Update,
                "tumor" => AppPermissions.KnowledgeTumorUpdate,
                _ => throw new ValidationException($"不支持的索引类型：{kind}（icd / cm3 / tumor）")
            };
            if (!RbacPolicies.HasPermission(httpContext.User, requiredPermission))
            {
                return Results.Forbid();
            }

            // 医院上下文必须存在：审计行落到假医院（原硬编码 GUID 兜底）会破坏数据隔离。
            if (requestContext.HospitalId == Guid.Empty)
            {
                return Results.BadRequest(ApiEnvelope.Create<object>(new { error = "HOSPITAL_CONTEXT_REQUIRED" }));
            }

            Action invalidate = kind switch
            {
                "icd" => () => app.Services.GetRequiredService<HospitalAi.Application.Coding.IcdIndex.IIcdIndexSource>().Invalidate(),
                "cm3" => () => app.Services.GetRequiredService<HospitalAi.Application.Coding.Cm3Index.ICm3IndexSource>().Invalidate(),
                "tumor" => () => app.Services.GetRequiredService<HospitalAi.Application.Coding.IcdIndex.ITumorIndexSource>().Invalidate(),
                _ => throw new ValidationException($"不支持的索引类型：{kind}（icd / cm3 / tumor）")
            };

            // 按具体类型取行并直接赋值：此前用反射 GetProperty("IsEnabled")!.SetValue，
            // 属性名拼错要到运行时才炸（NRE 500），编译期零保护。
            DateTimeOffset now = DateTimeOffset.UtcNow;
            switch (kind)
            {
                case "icd":
                {
                    var row = await db.IcdIndexes.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
                    if (row is null) { return IndexRowNotFound(); }
                    row.IsEnabled = request.Enabled;
                    row.UpdatedAt = now;
                    break;
                }
                case "cm3":
                {
                    var row = await db.Cm3Indexes.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
                    if (row is null) { return IndexRowNotFound(); }
                    row.IsEnabled = request.Enabled;
                    row.UpdatedAt = now;
                    break;
                }
                default:
                {
                    var row = await db.TumorIndexes.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
                    if (row is null) { return IndexRowNotFound(); }
                    row.IsEnabled = request.Enabled;
                    row.UpdatedAt = now;
                    break;
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            invalidate();

            // 审计留痕：停用/启用属于绕过默认行为的运维动作。
            db.AuditLogs.Add(new AuditLogRecord
            {
                Id = Guid.NewGuid(),
                HospitalId = requestContext.HospitalId,
                ResourceType = $"{kind}_index",
                ResourceId = id,
                Action = $"{kind}_index." + (request.Enabled ? "enable" : "disable"),
                Result = "SUCCESS",
                RequestId = requestContext.RequestId,
                ActorId = requestContext.UserId,
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ApiEnvelope.Create(new { id, enabled = request.Enabled }));

            static IResult IndexRowNotFound() =>
                Results.NotFound(ApiEnvelope.Create(new { message = "索引行不存在或已被删除。" }));
        })
        // 端点级只要求「至少有一种索引的更新权限」（具体是哪一种由端点内按 kind 二次判定）；
        // 若这里硬绑 icd.update，则只授 cm3/tumor 权限的角色会被挡在门外。
        .RequireAuthorization(RbacPolicies.KnowledgeIndexToggle);

    // ICD 索引库单行删除：有子节点时拒绝（避免父链悬空，先删/迁子节点）。
    app.MapDelete(
        "/api/v1/knowledge/icd-index/{id:guid}",
        async (
            Guid id,
            HospitalAiDbContext db,
            HospitalAi.Application.Coding.IcdIndex.IIcdIndexSource indexSource,
            CancellationToken cancellationToken) =>
        {
            var record = await db.IcdIndexes.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (record is null)
                return Results.NotFound(ApiEnvelope.Create(new { message = "索引行不存在或已被删除。" }));

            var childCount = await db.IcdIndexes.CountAsync(
                item => item.ParentSeqNo == record.SeqNo, cancellationToken);
            if (childCount > 0)
                throw new ValidationException($"该节点下还有 {childCount} 个子节点，请先删除或调整子节点。");

            db.IcdIndexes.Remove(record);
            await db.SaveChangesAsync(cancellationToken);
            indexSource.Invalidate();
            return Results.Ok(ApiEnvelope.Create(new { deleted = true }));
        }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.KnowledgeIcdDelete));

    // CM3（手术操作）索引库导入（按序号 upsert，全列保留）。
    app.MapPost(
        "/api/v1/cm3-index/import",
        async (
            ImportCm3IndexRequest request,
            HospitalAiDbContext db,
            HospitalAi.Application.Coding.Cm3Index.ICm3IndexSource cm3Source,
            CancellationToken cancellationToken) =>
        {
            var now = DateTimeOffset.UtcNow;
            var seqNos = request.Items.Select(item => item.SeqNo).ToArray();
            var existing = await db.Cm3Indexes
                .Where(item => seqNos.Contains(item.SeqNo))
                .ToDictionaryAsync(item => item.SeqNo, cancellationToken);

            var imported = 0;
            var updated = 0;
            foreach (var item in request.Items)
            {
                if (existing.TryGetValue(item.SeqNo, out var record))
                {
                    record.RawName = item.RawName;
                    record.RawNameEn = item.RawNameEn;
                    record.IndexName = item.IndexName;
                    record.IndexCode = item.IndexCode;
                    record.IndexLevel = item.IndexLevel;
                    record.ParentSeqNo = item.ParentSeqNo;
                    record.FullIndexName = item.FullIndexName;
                    record.LeadWordStatus = item.LeadWordStatus;
                    record.UpdatedAt = now;
                    updated++;
                }
                else
                {
                    db.Cm3Indexes.Add(new Cm3IndexRecord
                    {
                        Id = Guid.NewGuid(),
                        SeqNo = item.SeqNo,
                        RawName = item.RawName,
                        RawNameEn = item.RawNameEn,
                        IndexName = item.IndexName,
                        IndexCode = item.IndexCode,
                        IndexLevel = item.IndexLevel,
                        ParentSeqNo = item.ParentSeqNo,
                        FullIndexName = item.FullIndexName,
                        LeadWordStatus = item.LeadWordStatus,
                        CreatedAt = now,
                        UpdatedAt = now,
                    });
                    imported++;
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            // 与 ICD 索引导入对称：快照是单例内存缓存，不失效就会一直用导入前的旧树。
            cm3Source.Invalidate();
            return Results.Ok(ApiEnvelope.Create(
                new ImportCm3IndexResponse(imported, updated)));
        }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.KnowledgeCm3Import));

    // 查询 CM3 索引库（leadWordStatus：0=有效，1=无效，缺省返回全部）。
    app.MapGet(
        "/api/v1/knowledge/cm3-index",
        async (
            HospitalAiDbContext db,
            int? page,
            int? pageSize,
            string? keyword,
            int? leadWordStatus,
            int? indexLevel,
            CancellationToken cancellationToken) =>
        {
            var query = db.Cm3Indexes.AsQueryable();
            if (!string.IsNullOrEmpty(keyword))
            {
                query = query.Where(item =>
                    item.RawName.Contains(keyword)
                    || item.IndexName.Contains(keyword)
                    || item.FullIndexName.Contains(keyword)
                    || (item.IndexCode != null && item.IndexCode.Contains(keyword))
                    || (item.RawNameEn != null && item.RawNameEn.Contains(keyword)));
            }
            if (leadWordStatus is int status)
                query = query.Where(item => item.LeadWordStatus == status);
            if (indexLevel is int level)
                query = query.Where(item => item.IndexLevel == level);

            var total = await query.CountAsync(cancellationToken);
            var pageNum = page ?? 1;
            var size = Math.Min(pageSize ?? 20, 200);
            var items = await query
                .OrderBy(item => item.SeqNo)
                .Skip((pageNum - 1) * size)
                .Take(size)
                .Select(item => new
                {
                    item.Id,
                    item.SeqNo,
                    item.RawName,
                    item.RawNameEn,
                    item.IndexName,
                    item.IndexCode,
                    item.IndexLevel,
                    item.ParentSeqNo,
                    item.FullIndexName,
                    item.LeadWordStatus,
                    item.IsEnabled,
                })
                .ToListAsync(cancellationToken);

            return Results.Ok(ApiEnvelope.Create(new { total, page = pageNum, pageSize = size, items }));
        }).RequireAuthorization(RbacPolicies.KnowledgeReader);

    // CM3 索引库节点树：按父序号取直接子节点，并标记每个节点是否还有下一级。
    app.MapGet(
        "/api/v1/knowledge/cm3-index/children",
        async (
            HospitalAiDbContext db,
            int? parentSeqNo,
            int? leadWordStatus,
            int? offset,
            int? limit,
            CancellationToken cancellationToken) =>
        {
            var parentId = parentSeqNo ?? 0;
            var take = Math.Min(limit ?? 500, 1000);
            var query = db.Cm3Indexes.Where(item => item.ParentSeqNo == parentId);
            if (leadWordStatus is int status)
                query = query.Where(item => item.LeadWordStatus == status);

            var total = await query.CountAsync(cancellationToken);
            var children = await query
                .OrderBy(item => item.SeqNo)
                .Skip(offset ?? 0)
                .Take(take)
                .Select(item => new
                {
                    item.Id,
                    item.SeqNo,
                    item.RawName,
                    item.RawNameEn,
                    item.IndexName,
                    item.IndexCode,
                    item.IndexLevel,
                    item.ParentSeqNo,
                    item.FullIndexName,
                    item.LeadWordStatus,
                    item.IsEnabled,
                })
                .ToListAsync(cancellationToken);

            var seqNos = children.Select(item => item.SeqNo).ToArray();
            var parentsWithChildren = await db.Cm3Indexes
                .Where(item => seqNos.Contains(item.ParentSeqNo))
                .Select(item => item.ParentSeqNo)
                .Distinct()
                .ToListAsync(cancellationToken);
            var parentSet = parentsWithChildren.ToHashSet();

            var items = children.Select(item => new
            {
                item.Id,
                item.SeqNo,
                item.RawName,
                item.RawNameEn,
                item.IndexName,
                item.IndexCode,
                item.IndexLevel,
                item.ParentSeqNo,
                item.FullIndexName,
                item.LeadWordStatus,
                item.IsEnabled,
                hasChildren = parentSet.Contains(item.SeqNo),
            });

            return Results.Ok(ApiEnvelope.Create(new { parentSeqNo = parentId, total, items }));
        }).RequireAuthorization(RbacPolicies.KnowledgeReader);

    // CM3 索引库单行新增：与 ICD 索引库同一套校验（序号唯一、父节点存在、级别 0~9）。
    app.MapPost(
        "/api/v1/knowledge/cm3-index",
        async (
            IndexEntryInput input,
            HospitalAiDbContext db,
            HospitalAi.Application.Coding.Cm3Index.ICm3IndexSource cm3Source,
            CancellationToken cancellationToken) =>
        {
            var seqNo = input.SeqNo ?? 0;
            var indexName = (input.IndexName ?? string.Empty).Trim();
            var parentSeqNo = input.ParentSeqNo ?? 0;
            var indexLevel = input.IndexLevel ?? 0;
            var leadWordStatus = input.LeadWordStatus ?? 0;
            if (seqNo <= 0)
                throw new ValidationException("序号必须是正整数。");
            if (indexName.Length == 0 || indexName.Length > 200)
                throw new ValidationException("索引名称不能为空且不能超过 200 字。");
            if (parentSeqNo < 0)
                throw new ValidationException("父节点序号不能为负数。");
            if (indexLevel is < 0 or > 9)
                throw new ValidationException("索引级别只能是 0~9。");
            if (leadWordStatus is not (0 or 1))
                throw new ValidationException("主导词状态只能是 0（有效）或 1（无效）。");
            if (parentSeqNo > 0
                && !await db.Cm3Indexes.AnyAsync(item => item.SeqNo == parentSeqNo, cancellationToken))
            {
                throw new ValidationException($"父节点序号 {parentSeqNo} 不存在。");
            }

            if (await db.Cm3Indexes.AnyAsync(item => item.SeqNo == seqNo, cancellationToken))
                throw new ValidationException($"序号 {seqNo} 已存在，请换一个序号。");

            var now = DateTimeOffset.UtcNow;
            var rawName = (input.RawName ?? string.Empty).Trim();
            var record = new Cm3IndexRecord
            {
                Id = Guid.NewGuid(),
                SeqNo = seqNo,
                RawName = rawName.Length > 0 ? rawName : indexName,
                RawNameEn = IndexEditHelpers.TrimToNull(input.RawNameEn),
                IndexName = indexName,
                IndexCode = IndexEditHelpers.TrimToNull(input.IndexCode),
                IndexLevel = indexLevel,
                ParentSeqNo = parentSeqNo,
                FullIndexName = IndexEditHelpers.TrimToNull(input.FullIndexName) ?? indexName,
                LeadWordStatus = leadWordStatus,
                CreatedAt = now,
                UpdatedAt = now,
            };
            if (record.FullIndexName.Length > 300)
                throw new ValidationException("完整索引名称不能超过 300 字。");
            db.Cm3Indexes.Add(record);
            await db.SaveChangesAsync(cancellationToken);
            cm3Source.Invalidate();
            return Results.Ok(ApiEnvelope.Create(new { id = record.Id, seqNo = record.SeqNo }));
        }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.KnowledgeCm3Create));

    // CM3 索引库单行修改：序号不可改（父链按 seqNo 引用），其余字段为空表示沿用原值。
    app.MapPut(
        "/api/v1/knowledge/cm3-index/{id:guid}",
        async (
            Guid id,
            IndexEntryInput input,
            HospitalAiDbContext db,
            HospitalAi.Application.Coding.Cm3Index.ICm3IndexSource cm3Source,
            CancellationToken cancellationToken) =>
        {
            var record = await db.Cm3Indexes.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (record is null)
                return Results.NotFound(ApiEnvelope.Create(new { message = "索引行不存在或已被删除。" }));

            var indexName = IndexEditHelpers.TrimToNull(input.IndexName) ?? record.IndexName;
            if (indexName.Length > 200)
                throw new ValidationException("索引名称不能超过 200 字。");
            var parentSeqNo = input.ParentSeqNo ?? record.ParentSeqNo;
            if (parentSeqNo < 0)
                throw new ValidationException("父节点序号不能为负数。");
            if (parentSeqNo > 0 && parentSeqNo != record.ParentSeqNo
                && !await db.Cm3Indexes.AnyAsync(item => item.SeqNo == parentSeqNo, cancellationToken))
            {
                throw new ValidationException($"父节点序号 {parentSeqNo} 不存在。");
            }
            if (input.IndexLevel is int level && level is < 0 or > 9)
                throw new ValidationException("索引级别只能是 0~9。");
            if (input.LeadWordStatus is int status && status is not (0 or 1))
                throw new ValidationException("主导词状态只能是 0（有效）或 1（无效）。");

            await IndexEditHelpers.EnsureNoIndexParentCycleAsync(
                record.SeqNo,
                parentSeqNo,
                seq => db.Cm3Indexes.Where(item => item.SeqNo == seq)
                    .Select(item => (int?)item.ParentSeqNo).FirstOrDefaultAsync(cancellationToken),
                cancellationToken);

            record.RawName = IndexEditHelpers.TrimToNull(input.RawName) ?? record.RawName;
            record.RawNameEn = input.RawNameEn is null ? record.RawNameEn : IndexEditHelpers.TrimToNull(input.RawNameEn);
            record.IndexName = indexName;
            record.IndexCode = input.IndexCode is null ? record.IndexCode : IndexEditHelpers.TrimToNull(input.IndexCode);
            record.IndexLevel = input.IndexLevel ?? record.IndexLevel;
            record.ParentSeqNo = parentSeqNo;
            record.FullIndexName = IndexEditHelpers.TrimToNull(input.FullIndexName) ?? record.FullIndexName;
            if (record.FullIndexName.Length > 300)
                throw new ValidationException("完整索引名称不能超过 300 字。");
            record.LeadWordStatus = input.LeadWordStatus ?? record.LeadWordStatus;
            record.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            cm3Source.Invalidate();
            return Results.Ok(ApiEnvelope.Create(new { id = record.Id, seqNo = record.SeqNo }));
        }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.KnowledgeCm3Update));

    // CM3 索引库单行删除：有子节点时拒绝（避免父链悬空，先删/迁子节点）。
    app.MapDelete(
        "/api/v1/knowledge/cm3-index/{id:guid}",
        async (
            Guid id,
            HospitalAiDbContext db,
            HospitalAi.Application.Coding.Cm3Index.ICm3IndexSource cm3Source,
            CancellationToken cancellationToken) =>
        {
            var record = await db.Cm3Indexes.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (record is null)
                return Results.NotFound(ApiEnvelope.Create(new { message = "索引行不存在或已被删除。" }));

            var childCount = await db.Cm3Indexes.CountAsync(
                item => item.ParentSeqNo == record.SeqNo, cancellationToken);
            if (childCount > 0)
                throw new ValidationException($"该节点下还有 {childCount} 个子节点，请先删除或调整子节点。");

            db.Cm3Indexes.Remove(record);
            await db.SaveChangesAsync(cancellationToken);
            cm3Source.Invalidate();
            return Results.Ok(ApiEnvelope.Create(new { deleted = true }));
        }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.KnowledgeCm3Delete));

    // 查询肿瘤索引库（"肿瘤，肿瘤性"部位表：每行并排五个动态码，无主导词状态列）。

    app.MapGet(
        "/api/v1/knowledge/tumor-index",
        async (
            HospitalAiDbContext db,
            int? page,
            int? pageSize,
            string? keyword,
            int? indexLevel,
            CancellationToken cancellationToken) =>
        {
            var query = db.TumorIndexes.AsQueryable();
            if (!string.IsNullOrEmpty(keyword))
            {
                query = query.Where(item =>
                    item.IndexName.Contains(keyword)
                    || item.FullIndexName.Contains(keyword)
                    || (item.IndexNameEn != null && item.IndexNameEn.Contains(keyword))
                    || (item.CodeMalignantPrimary != null && item.CodeMalignantPrimary.Contains(keyword))
                    || (item.CodeMalignantSecondary != null && item.CodeMalignantSecondary.Contains(keyword))
                    || (item.CodeInSitu != null && item.CodeInSitu.Contains(keyword))
                    || (item.CodeBenign != null && item.CodeBenign.Contains(keyword))
                    || (item.CodeUncertain != null && item.CodeUncertain.Contains(keyword))
                    || (item.JumpTarget != null && item.JumpTarget.Contains(keyword)));
            }
            if (indexLevel is int level)
                query = query.Where(item => item.IndexLevel == level);

            var total = await query.CountAsync(cancellationToken);
            var pageNum = page ?? 1;
            var size = Math.Min(pageSize ?? 20, 200);
            var items = await query
                .OrderBy(item => item.SeqNo)
                .Skip((pageNum - 1) * size)
                .Take(size)
                .Select(item => new
                {
                    item.Id,
                    item.SeqNo,
                    item.IndexName,
                    item.IndexNameEn,
                    item.IndexLevel,
                    item.ParentSeqNo,
                    item.FullIndexName,
                    item.CodeMalignantPrimary,
                    item.CodeMalignantSecondary,
                    item.CodeInSitu,
                    item.CodeBenign,
                    item.CodeUncertain,
                    item.JumpKind,
                    item.JumpTarget,
                    item.IsEnabled,
                })
                .ToListAsync(cancellationToken);

            return Results.Ok(ApiEnvelope.Create(new { total, page = pageNum, pageSize = size, items }));
        }).RequireAuthorization(RbacPolicies.KnowledgeReader);

    // 肿瘤索引库节点树：按父序号取直接子节点，并标记每个节点是否还有下一级。
    app.MapGet(
        "/api/v1/knowledge/tumor-index/children",
        async (
            HospitalAiDbContext db,
            int? parentSeqNo,
            int? offset,
            int? limit,
            CancellationToken cancellationToken) =>
        {
            var parentId = parentSeqNo ?? 0;
            var take = Math.Min(limit ?? 500, 1000);
            var query = db.TumorIndexes.Where(item => item.ParentSeqNo == parentId);

            var total = await query.CountAsync(cancellationToken);
            var children = await query
                .OrderBy(item => item.SeqNo)
                .Skip(offset ?? 0)
                .Take(take)
                .Select(item => new
                {
                    item.Id,
                    item.SeqNo,
                    item.IndexName,
                    item.IndexNameEn,
                    item.IndexLevel,
                    item.ParentSeqNo,
                    item.FullIndexName,
                    item.CodeMalignantPrimary,
                    item.CodeMalignantSecondary,
                    item.CodeInSitu,
                    item.CodeBenign,
                    item.CodeUncertain,
                    item.JumpKind,
                    item.JumpTarget,
                    item.IsEnabled,
                })
                .ToListAsync(cancellationToken);

            var seqNos = children.Select(item => item.SeqNo).ToArray();
            var parentsWithChildren = await db.TumorIndexes
                .Where(item => seqNos.Contains(item.ParentSeqNo))
                .Select(item => item.ParentSeqNo)
                .Distinct()
                .ToListAsync(cancellationToken);
            var parentSet = parentsWithChildren.ToHashSet();

            var items = children.Select(item => new
            {
                item.Id,
                item.SeqNo,
                item.IndexName,
                item.IndexNameEn,
                item.IndexLevel,
                item.ParentSeqNo,
                item.FullIndexName,
                item.CodeMalignantPrimary,
                item.CodeMalignantSecondary,
                item.CodeInSitu,
                item.CodeBenign,
                item.CodeUncertain,
                item.JumpKind,
                item.JumpTarget,
                item.IsEnabled,
                hasChildren = parentSet.Contains(item.SeqNo),
            });

            return Results.Ok(ApiEnvelope.Create(new { parentSeqNo = parentId, total, items }));
        }).RequireAuthorization(RbacPolicies.KnowledgeReader);

    // 肿瘤索引单行新增：序号唯一、父节点存在、跳转类型为 见/另见 时必须给目标词。
    app.MapPost(
        "/api/v1/knowledge/tumor-index",
        async (
            TumorIndexEntryInput input,
            HospitalAiDbContext db,
            HospitalAi.Application.Coding.IcdIndex.ITumorIndexSource tumorSource,
            CancellationToken cancellationToken) =>
        {
            var seqNo = input.SeqNo ?? 0;
            var indexName = (input.IndexName ?? string.Empty).Trim();
            var parentSeqNo = input.ParentSeqNo ?? 0;
            var indexLevel = input.IndexLevel ?? 0;
            var jumpKind = input.JumpKind ?? 0;
            var jumpTarget = IndexEditHelpers.TrimToNull(input.JumpTarget);
            if (seqNo <= 0)
                throw new ValidationException("序号必须是正整数。");
            if (indexName.Length == 0 || indexName.Length > 200)
                throw new ValidationException("索引名称不能为空且不能超过 200 字。");
            if (parentSeqNo < 0)
                throw new ValidationException("父节点序号不能为负数。");
            if (indexLevel is < 0 or > 9)
                throw new ValidationException("索引级别只能是 0~9。");
            if (jumpKind is not (0 or 1 or 2))
                throw new ValidationException("跳转类型只能是 无跳转/见/另见。");
            if (jumpKind != 0 && jumpTarget is null)
                throw new ValidationException("跳转类型为「见」或「另见」时必须填写跳转目标词。");
            if (parentSeqNo > 0
                && !await db.TumorIndexes.AnyAsync(item => item.SeqNo == parentSeqNo, cancellationToken))
            {
                throw new ValidationException($"父节点序号 {parentSeqNo} 不存在。");
            }

            if (await db.TumorIndexes.AnyAsync(item => item.SeqNo == seqNo, cancellationToken))
                throw new ValidationException($"序号 {seqNo} 已存在，请换一个序号。");

            var now = DateTimeOffset.UtcNow;
            var record = new TumorIndexRecord
            {
                Id = Guid.NewGuid(),
                SeqNo = seqNo,
                IndexName = indexName,
                IndexNameEn = IndexEditHelpers.TrimToNull(input.IndexNameEn),
                IndexLevel = indexLevel,
                ParentSeqNo = parentSeqNo,
                FullIndexName = IndexEditHelpers.TrimToNull(input.FullIndexName) ?? indexName,
                CodeMalignantPrimary = IndexEditHelpers.TrimToNull(input.CodeMalignantPrimary),
                CodeMalignantSecondary = IndexEditHelpers.TrimToNull(input.CodeMalignantSecondary),
                CodeInSitu = IndexEditHelpers.TrimToNull(input.CodeInSitu),
                CodeBenign = IndexEditHelpers.TrimToNull(input.CodeBenign),
                CodeUncertain = IndexEditHelpers.TrimToNull(input.CodeUncertain),
                JumpKind = jumpKind,
                JumpTarget = jumpKind == 0 ? null : jumpTarget,
                CreatedAt = now,
                UpdatedAt = now,
            };
            if (record.FullIndexName.Length > 300)
                throw new ValidationException("完整索引名称不能超过 300 字。");
            db.TumorIndexes.Add(record);
            await db.SaveChangesAsync(cancellationToken);
            tumorSource.Invalidate();
            return Results.Ok(ApiEnvelope.Create(new { id = record.Id, seqNo = record.SeqNo }));
        }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.KnowledgeTumorCreate));

    // 肿瘤索引单行修改：序号不可改（父链按 seqNo 引用），其余字段为空表示沿用原值。
    app.MapPut(
        "/api/v1/knowledge/tumor-index/{id:guid}",
        async (
            Guid id,
            TumorIndexEntryInput input,
            HospitalAiDbContext db,
            HospitalAi.Application.Coding.IcdIndex.ITumorIndexSource tumorSource,
            CancellationToken cancellationToken) =>
        {
            var record = await db.TumorIndexes.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (record is null)
                return Results.NotFound(ApiEnvelope.Create(new { message = "索引行不存在或已被删除。" }));

            var indexName = IndexEditHelpers.TrimToNull(input.IndexName) ?? record.IndexName;
            if (indexName.Length > 200)
                throw new ValidationException("索引名称不能超过 200 字。");
            var parentSeqNo = input.ParentSeqNo ?? record.ParentSeqNo;
            if (parentSeqNo < 0)
                throw new ValidationException("父节点序号不能为负数。");
            if (parentSeqNo > 0 && parentSeqNo != record.ParentSeqNo
                && !await db.TumorIndexes.AnyAsync(item => item.SeqNo == parentSeqNo, cancellationToken))
            {
                throw new ValidationException($"父节点序号 {parentSeqNo} 不存在。");
            }
            if (input.IndexLevel is int level && level is < 0 or > 9)
                throw new ValidationException("索引级别只能是 0~9。");
            if (input.JumpKind is int kind && kind is not (0 or 1 or 2))
                throw new ValidationException("跳转类型只能是 无跳转/见/另见。");

            await IndexEditHelpers.EnsureNoIndexParentCycleAsync(
                record.SeqNo,
                parentSeqNo,
                seq => db.TumorIndexes.Where(item => item.SeqNo == seq)
                    .Select(item => (int?)item.ParentSeqNo).FirstOrDefaultAsync(cancellationToken),
                cancellationToken);

            var jumpKind = input.JumpKind ?? record.JumpKind;
            var jumpTarget = input.JumpTarget is null ? record.JumpTarget : IndexEditHelpers.TrimToNull(input.JumpTarget);
            if (jumpKind != 0 && jumpTarget is null)
                throw new ValidationException("跳转类型为「见」或「另见」时必须填写跳转目标词。");

            record.IndexName = indexName;
            record.IndexNameEn = input.IndexNameEn is null ? record.IndexNameEn : IndexEditHelpers.TrimToNull(input.IndexNameEn);
            record.IndexLevel = input.IndexLevel ?? record.IndexLevel;
            record.ParentSeqNo = parentSeqNo;
            record.FullIndexName = IndexEditHelpers.TrimToNull(input.FullIndexName) ?? record.FullIndexName;
            if (record.FullIndexName.Length > 300)
                throw new ValidationException("完整索引名称不能超过 300 字。");
            record.CodeMalignantPrimary = input.CodeMalignantPrimary is null
                ? record.CodeMalignantPrimary
                : IndexEditHelpers.TrimToNull(input.CodeMalignantPrimary);
            record.CodeMalignantSecondary = input.CodeMalignantSecondary is null
                ? record.CodeMalignantSecondary
                : IndexEditHelpers.TrimToNull(input.CodeMalignantSecondary);
            record.CodeInSitu = input.CodeInSitu is null ? record.CodeInSitu : IndexEditHelpers.TrimToNull(input.CodeInSitu);
            record.CodeBenign = input.CodeBenign is null ? record.CodeBenign : IndexEditHelpers.TrimToNull(input.CodeBenign);
            record.CodeUncertain = input.CodeUncertain is null
                ? record.CodeUncertain
                : IndexEditHelpers.TrimToNull(input.CodeUncertain);
            record.JumpKind = jumpKind;
            record.JumpTarget = jumpKind == 0 ? null : jumpTarget;
            record.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            tumorSource.Invalidate();
            return Results.Ok(ApiEnvelope.Create(new { id = record.Id, seqNo = record.SeqNo }));
        }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.KnowledgeTumorUpdate));

    // 肿瘤索引单行删除：有子节点时拒绝（避免父链悬空，先删/迁子节点）。
    app.MapDelete(
        "/api/v1/knowledge/tumor-index/{id:guid}",
        async (
            Guid id,
            HospitalAiDbContext db,
            HospitalAi.Application.Coding.IcdIndex.ITumorIndexSource tumorSource,
            CancellationToken cancellationToken) =>
        {
            var record = await db.TumorIndexes.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (record is null)
                return Results.NotFound(ApiEnvelope.Create(new { message = "索引行不存在或已被删除。" }));

            var childCount = await db.TumorIndexes.CountAsync(
                item => item.ParentSeqNo == record.SeqNo, cancellationToken);
            if (childCount > 0)
                throw new ValidationException($"该节点下还有 {childCount} 个子节点，请先删除或调整子节点。");

            db.TumorIndexes.Remove(record);
            await db.SaveChangesAsync(cancellationToken);
            tumorSource.Invalidate();
            return Results.Ok(ApiEnvelope.Create(new { deleted = true }));
        }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.KnowledgeTumorDelete));

    app.MapPost(
        "/api/v1/icd-term-synonym/import",
        async (
            ImportTermSynonymRequest request,
            HospitalAiDbContext db,
            HospitalAi.Application.Coding.Matching.IIcdTermSynonymSource synonymSource,
            CancellationToken cancellationToken) =>
        {
            var now = DateTimeOffset.UtcNow;
            var pairs = request.Items
                .Where(item => !string.IsNullOrWhiteSpace(item.CanonicalTerm))
                .SelectMany(item => item.Variants
                    .Where(variant => !string.IsNullOrWhiteSpace(variant))
                    .Select(variant => new { item.TermType, Canonical = item.CanonicalTerm.Trim(), Variant = variant.Trim() }))
                .GroupBy(pair => new { pair.TermType, pair.Variant })
                .Select(group => group.First())
                .ToList();

            var typeKeys = pairs.Select(pair => pair.TermType).Distinct().ToArray();
            var variantKeys = pairs.Select(pair => pair.Variant).ToArray();
            var existing = await db.IcdTermSynonyms
                .Where(item => typeKeys.Contains(item.TermType) && variantKeys.Contains(item.VariantTerm))
                .ToListAsync(cancellationToken);
            var existingMap = existing.ToDictionary(item => (item.TermType, item.VariantTerm));

            var imported = 0;
            var updated = 0;
            foreach (var pair in pairs)
            {
                if (existingMap.TryGetValue((pair.TermType, pair.Variant), out var record))
                {
                    if (!record.IsEnabled || record.CanonicalTerm != pair.Canonical)
                    {
                        record.CanonicalTerm = pair.Canonical;
                        record.IsEnabled = true;
                        record.UpdatedAt = now;
                        updated++;
                    }
                }
                else
                {
                    db.IcdTermSynonyms.Add(new IcdTermSynonymRecord
                    {
                        Id = Guid.NewGuid(),
                        TermType = pair.TermType,
                        CanonicalTerm = pair.Canonical,
                        VariantTerm = pair.Variant,
                        IsEnabled = true,
                        CreatedAt = now,
                        UpdatedAt = now,
                    });
                    imported++;
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            synonymSource.Invalidate();
            return Results.Ok(ApiEnvelope.Create(new ImportTermSynonymResponse(imported, updated)));
        }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.KnowledgeSynonymImport));

    // 术语同义词组查询（按 类型+规范词 聚合展示，停用行一并返回供页面维护）。
    app.MapGet(
        "/api/v1/knowledge/icd-term-synonym",
        async (
            string? termType,
            string? keyword,
            HospitalAiDbContext db,
            CancellationToken cancellationToken) =>
        {
            var query = db.IcdTermSynonyms.AsQueryable();
            if (!string.IsNullOrEmpty(termType))
                query = query.Where(item => item.TermType == termType);
            if (!string.IsNullOrEmpty(keyword))
                query = query.Where(item =>
                    item.CanonicalTerm.Contains(keyword) || item.VariantTerm.Contains(keyword));

            var rows = await query
                .OrderBy(item => item.TermType)
                .ThenBy(item => item.CanonicalTerm)
                .ThenBy(item => item.VariantTerm)
                .ToListAsync(cancellationToken);

            var groups = rows
                .GroupBy(item => new { item.TermType, item.CanonicalTerm })
                .Select(group => new
                {
                    termType = group.Key.TermType,
                    canonicalTerm = group.Key.CanonicalTerm,
                    variants = group
                        .Select(item => new { item.Id, item.VariantTerm, item.IsEnabled })
                        .ToList(),
                })
                .ToList();

            return Results.Ok(ApiEnvelope.Create(new { total = groups.Count, items = groups }));
        }).RequireAuthorization(RbacPolicies.KnowledgeReader);

    // 单条同义词新增：在等价类里补一个等同词（类内任意两词在匹配时可互换）。
    app.MapPost(
        "/api/v1/knowledge/icd-term-synonym",
        async (
            IcdTermSynonymInput input,
            HospitalAiDbContext db,
            HospitalAi.Application.Coding.Matching.IIcdTermSynonymSource synonymSource,
            CancellationToken cancellationToken) =>
        {
            var termType = (input.TermType ?? string.Empty).Trim().ToUpperInvariant();
            var canonical = (input.CanonicalTerm ?? string.Empty).Trim();
            var variant = (input.VariantTerm ?? string.Empty).Trim();
            if (termType != HospitalAi.Application.Coding.Matching.TermSynonymTypes.Diagnosis
                && termType != HospitalAi.Application.Coding.Matching.TermSynonymTypes.Procedure)
            {
                throw new ValidationException("termType 只能是 DIAGNOSIS（诊断）或 PROCEDURE（手术）。");
            }

            if (canonical.Length == 0 || variant.Length == 0)
            {
                throw new ValidationException("规范词与等同词都不能为空。");
            }

            if (string.Equals(canonical, variant, StringComparison.Ordinal))
            {
                throw new ValidationException("等同词与规范词相同，无需登记。");
            }

            // (term_type, variant_term) 是库内唯一索引：一个等同词只能归属一个等价类。
            if (await db.IcdTermSynonyms.AnyAsync(
                    item => item.TermType == termType && item.VariantTerm == variant,
                    cancellationToken))
            {
                throw new ValidationException($"「{variant}」在 {termType} 通道已登记，请直接编辑该条或换用其它规范词。");
            }

            var now = DateTimeOffset.UtcNow;
            var record = new IcdTermSynonymRecord
            {
                Id = Guid.NewGuid(),
                TermType = termType,
                CanonicalTerm = canonical,
                VariantTerm = variant,
                IsEnabled = input.IsEnabled ?? true,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.IcdTermSynonyms.Add(record);
            await db.SaveChangesAsync(cancellationToken);
            synonymSource.Invalidate();
            return Results.Ok(ApiEnvelope.Create(new { id = record.Id }));
        }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.KnowledgeSynonymCreate));

    // 单条同义词修改：改等同词/规范词、换通道或启停（null 字段沿用原值）。
    app.MapPut(
        "/api/v1/knowledge/icd-term-synonym/{id:guid}",
        async (
            Guid id,
            IcdTermSynonymInput input,
            HospitalAiDbContext db,
            HospitalAi.Application.Coding.Matching.IIcdTermSynonymSource synonymSource,
            CancellationToken cancellationToken) =>
        {
            var record = await db.IcdTermSynonyms.FirstOrDefaultAsync(
                item => item.Id == id, cancellationToken);
            if (record is null)
            {
                return Results.NotFound(ApiEnvelope.Create(new { message = "资源不存在或已被删除。" }));
            }

            var termType = string.IsNullOrWhiteSpace(input.TermType)
                ? record.TermType
                : input.TermType.Trim().ToUpperInvariant();
            if (termType != HospitalAi.Application.Coding.Matching.TermSynonymTypes.Diagnosis
                && termType != HospitalAi.Application.Coding.Matching.TermSynonymTypes.Procedure)
            {
                throw new ValidationException("termType 只能是 DIAGNOSIS（诊断）或 PROCEDURE（手术）。");
            }

            var canonical = string.IsNullOrWhiteSpace(input.CanonicalTerm)
                ? record.CanonicalTerm
                : input.CanonicalTerm.Trim();
            var variant = string.IsNullOrWhiteSpace(input.VariantTerm)
                ? record.VariantTerm
                : input.VariantTerm.Trim();
            if (canonical.Length == 0 || variant.Length == 0)
            {
                throw new ValidationException("规范词与等同词都不能为空。");
            }

            if (string.Equals(canonical, variant, StringComparison.Ordinal))
            {
                throw new ValidationException("等同词与规范词相同，无需登记。");
            }

            if (await db.IcdTermSynonyms.AnyAsync(
                    item => item.Id != id && item.TermType == termType && item.VariantTerm == variant,
                    cancellationToken))
            {
                throw new ValidationException($"「{variant}」在 {termType} 通道已登记，一个等同词只能归属一个等价类。");
            }

            record.TermType = termType;
            record.CanonicalTerm = canonical;
            record.VariantTerm = variant;
            record.IsEnabled = input.IsEnabled ?? record.IsEnabled;
            record.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            synonymSource.Invalidate();
            return Results.Ok(ApiEnvelope.Create(new { id = record.Id, updated = true }));
        }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.KnowledgeSynonymUpdate));

    app.MapDelete(
        "/api/v1/knowledge/icd-term-synonym/{id:guid}",
        async (
            Guid id,
            HospitalAiDbContext db,
            HospitalAi.Application.Coding.Matching.IIcdTermSynonymSource synonymSource,
            CancellationToken cancellationToken) =>
        {
            var record = await db.IcdTermSynonyms.FirstOrDefaultAsync(
                item => item.Id == id, cancellationToken);
            if (record is null)
            {
                return Results.NotFound(ApiEnvelope.Create(new { message = "资源不存在或已被删除。" }));
            }

            db.IcdTermSynonyms.Remove(record);
            await db.SaveChangesAsync(cancellationToken);
            synonymSource.Invalidate();
            return Results.Ok(ApiEnvelope.Create(new { deleted = true }));
        }).RequireAuthorization(RbacPolicies.PermissionPolicy(AppPermissions.KnowledgeSynonymDelete));

        return app;
    }
}