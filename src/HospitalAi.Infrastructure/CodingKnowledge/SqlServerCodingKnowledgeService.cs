using System.Text.Json;
using HospitalAi.Application.Abstractions;
using HospitalAi.Application.Coding.Matching;
using HospitalAi.Application.Common;
using HospitalAi.Contracts.CodingKnowledge;
using HospitalAi.Infrastructure.SqlServer;
using Microsoft.EntityFrameworkCore;

namespace HospitalAi.Infrastructure.CodingKnowledge;

/// <summary>
/// 基于 SQL Server 的编码知识库导入服务。
/// </summary>
public sealed class SqlServerCodingKnowledgeService(
    HospitalAiDbContext dbContext,
    IRequestContext requestContext) : ICodingKnowledgeService
{
    public async Task<ImportCodeSystemResponse> ImportCodeSystemAsync(
        ImportCodeSystemRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureHospital();
        if (string.IsNullOrWhiteSpace(request.CodeSystem))
        {
            throw new ValidationException("编码体系不能为空。");
        }

        var now = DateTimeOffset.UtcNow;
        var codeSystem = await dbContext.CodeSystems.SingleOrDefaultAsync(
            item => item.HospitalId == requestContext.HospitalId
                && item.Code == request.CodeSystem,
            cancellationToken);
        if (codeSystem is null)
        {
            codeSystem = new CodeSystemRecord
            {
                Id = Guid.NewGuid(),
                HospitalId = requestContext.HospitalId,
                Code = request.CodeSystem,
                Name = request.CodeSystem,
                Version = request.Version,
                CreatedAt = now,
                UpdatedAt = now
            };
            dbContext.CodeSystems.Add(codeSystem);
        }
        else
        {
            codeSystem.Version = request.Version;
            codeSystem.UpdatedAt = now;
        }

        var itemsByCode = new Dictionary<string, MedicalCodeImportItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in request.Codes)
        {
            if (string.IsNullOrWhiteSpace(item.Code) || string.IsNullOrWhiteSpace(item.Title))
            {
                throw new ValidationException("编码和名称不能为空。");
            }

            // 同一导入批次可能来自多份对照资料，重复编码以最后一条清洗结果为准。
            itemsByCode[item.Code.Trim()] = item;
        }

        // 不使用 Contains(keys) 生成 OPENJSON，兼容 SQL Server 较低数据库兼容级别。
        var existingCodes = (await dbContext.MedicalCodes
                .Where(code => code.HospitalId == requestContext.HospitalId
                    && code.CodeSystemCode == request.CodeSystem)
                .ToListAsync(cancellationToken))
            .ToDictionary(code => code.Code, StringComparer.OrdinalIgnoreCase);

        var imported = 0;
        var updated = 0;
        foreach (var item in itemsByCode.Values)
        {
            var code = item.Code.Trim();
            if (!existingCodes.TryGetValue(code, out var existing))
            {
                imported++;
                dbContext.MedicalCodes.Add(new MedicalCodeRecord
                {
                    Id = Guid.NewGuid(),
                    HospitalId = requestContext.HospitalId,
                    CodeSystem = codeSystem,
                    CodeSystemCode = request.CodeSystem,
                    Code = code,
                    Title = item.Title,
                    CodeType = item.CodeType,
                    SearchText = item.SearchText ?? item.Title,
                    IsEnabled = item.IsEnabled,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                continue;
            }

            updated++;
            existing.CodeSystem = codeSystem;
            existing.CodeSystemCode = request.CodeSystem;
            existing.Title = item.Title;
            existing.CodeType = item.CodeType;
            existing.SearchText = item.SearchText ?? item.Title;
            existing.IsEnabled = item.IsEnabled;
            existing.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new ImportCodeSystemResponse(request.CodeSystem, imported, updated);
    }

    public async Task<ImportCodingRulesResponse> ImportCodingRulesAsync(
        ImportCodingRulesRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureHospital();
        var now = DateTimeOffset.UtcNow;
        var synonymsByKey = new Dictionary<
            (string Term, string EntityType, string CodeSystemCode, string Code),
            TermSynonymImportItem>();
        foreach (var item in request.Synonyms)
        {
            var term = item.Term.Trim();
            var entityType = item.EntityType.Trim();
            var codeSystemCode = string.IsNullOrWhiteSpace(item.CodeSystemCode)
                ? "ICD-10"
                : item.CodeSystemCode.Trim();
            var code = item.Code.Trim();
            if (string.IsNullOrWhiteSpace(term)
                || string.IsNullOrWhiteSpace(item.NormalizedTerm))
            {
                throw new ValidationException("同义词和归一化术语不能为空。");
            }

            synonymsByKey[(term, entityType, codeSystemCode, code)] = item with
            {
                Term = term,
                EntityType = entityType,
                CodeSystemCode = codeSystemCode,
                Code = code
            };
        }

        // 先按医院读取已有数据，再在内存中按复合键匹配，避免大批量 Contains 触发 OPENJSON。
        var existingSynonyms = (await dbContext.TermSynonyms
                .Where(item => item.HospitalId == requestContext.HospitalId)
                .ToListAsync(cancellationToken))
            .ToDictionary(
                item => (item.Term, item.EntityType, item.CodeSystemCode, item.Code),
                SynonymKeyComparer.Instance);

        foreach (var pair in synonymsByKey)
        {
            var item = pair.Value;
            if (!existingSynonyms.TryGetValue(pair.Key, out var existing))
            {
                dbContext.TermSynonyms.Add(new TermSynonymRecord
                {
                    Id = Guid.NewGuid(),
                    HospitalId = requestContext.HospitalId,
                    Term = pair.Key.Term,
                    NormalizedTerm = item.NormalizedTerm,
                    CodeSystemCode = pair.Key.CodeSystemCode,
                    Code = pair.Key.Code,
                    EntityType = pair.Key.EntityType,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                continue;
            }

            existing.NormalizedTerm = item.NormalizedTerm;
            existing.CodeSystemCode = pair.Key.CodeSystemCode;
            existing.Code = pair.Key.Code;
            existing.UpdatedAt = now;
        }

        await UpsertIndexChannelSynonymsAsync(synonymsByKey.Values, now, cancellationToken);

        var rulesByCode = new Dictionary<string, CodingRuleImportItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in request.Rules)
        {
            if (string.IsNullOrWhiteSpace(item.RuleCode))
            {
                throw new ValidationException("规则编码不能为空。");
            }

            // 条件与动作只做 JSON 合法性校验并原样存储，禁止动态执行任意表达式或脚本。
            if (item.ConditionJson is not null)
            {
                ValidateRuleJson(item.ConditionJson, "conditionJson");
            }

            if (item.ActionJson is not null)
            {
                ValidateRuleJson(item.ActionJson, "actionJson");
            }

            rulesByCode[item.RuleCode.Trim()] = item;
        }

        var existingRules = (await dbContext.CodingRules
                .Where(item => item.HospitalId == requestContext.HospitalId)
                .ToListAsync(cancellationToken))
            .ToDictionary(item => item.RuleCode, StringComparer.OrdinalIgnoreCase);

        foreach (var pair in rulesByCode)
        {
            var item = new CodingRuleImportItem(
                pair.Value.RuleCode.Trim(),
                pair.Value.CodeSystem,
                pair.Value.CodePattern,
                pair.Value.RuleType,
                pair.Value.Severity,
                pair.Value.Message,
                pair.Value.IsEnabled,
                string.IsNullOrWhiteSpace(pair.Value.RuleVersion) ? "v1" : pair.Value.RuleVersion.Trim(),
                pair.Value.Priority,
                string.IsNullOrWhiteSpace(pair.Value.Group) ? null : pair.Value.Group.Trim(),
                pair.Value.ConditionJson,
                pair.Value.ActionJson,
                pair.Value.Blocking,
                pair.Value.IsBuiltin);

            if (!existingRules.TryGetValue(pair.Key, out var existing))
            {
                dbContext.CodingRules.Add(new CodingRuleRecord
                {
                    Id = Guid.NewGuid(),
                    HospitalId = requestContext.HospitalId,
                    RuleCode = pair.Key,
                    CodeSystemCode = item.CodeSystem,
                    CodePattern = item.CodePattern,
                    RuleType = item.RuleType,
                    Severity = item.Severity,
                    Message = item.Message,
                    RuleVersion = item.RuleVersion,
                    Priority = item.Priority,
                    RuleGroup = item.Group,
                    ConditionJson = item.ConditionJson,
                    ActionJson = item.ActionJson,
                    Blocking = item.Blocking,
                    IsBuiltin = item.IsBuiltin,
                    IsEnabled = item.IsEnabled,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                continue;
            }

            existing.CodeSystemCode = item.CodeSystem;
            existing.CodePattern = item.CodePattern;
            existing.RuleType = item.RuleType;
            existing.Severity = item.Severity;
            existing.Message = item.Message;
            existing.RuleVersion = item.RuleVersion;
            existing.Priority = item.Priority;
            existing.RuleGroup = item.Group;
            existing.ConditionJson = item.ConditionJson;
            existing.ActionJson = item.ActionJson;
            existing.Blocking = item.Blocking;
            existing.IsBuiltin = item.IsBuiltin;
            existing.IsEnabled = item.IsEnabled;
            existing.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new ImportCodingRulesResponse(synonymsByKey.Count, rulesByCode.Count);
    }

    /// <summary>
    /// 同义词同时落到索引链路词库 icd_term_synonym（推荐链路实际读取的表，平台级共享）：
    /// entityType 决定通道（DISEASE/DIAGNOSIS→诊断，PROCEDURE/OPERATION→手术），
    /// 键为 (term_type, variant_term)，与库内唯一索引一致；codeSystemCode/code 不参与链路匹配，故不落库。
    /// term_synonym 仍按医院写入，供 ES 索引与旧版检索通道使用。
    /// </summary>
    private async Task UpsertIndexChannelSynonymsAsync(
        IEnumerable<TermSynonymImportItem> items,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var pairs = items
            .Select(item => (
                TermType: ToTermChannel(item.EntityType),
                Canonical: item.NormalizedTerm.Trim(),
                Variant: item.Term.Trim()))
            .Where(pair => pair.Canonical.Length > 0
                && pair.Variant.Length > 0
                && !string.Equals(pair.Canonical, pair.Variant, StringComparison.Ordinal))
            .GroupBy(pair => (pair.TermType, pair.Variant), ValueTupleComparer.Instance)
            .Select(group => group.First())
            .ToList();
        if (pairs.Count == 0)
        {
            return;
        }

        var termTypes = pairs.Select(pair => pair.TermType).Distinct().ToArray();
        var variants = pairs.Select(pair => pair.Variant).ToArray();
        // 库内比对按 SQL 大小写不敏感 collation 走，这里同样用不区分大小写的键，
        // 避免 "II"/"ii" 被判成新词后撞上 (term_type, variant_term) 唯一索引。
        // EF.Constant 强制把集合内联为 IN 常量列表：参数化集合默认翻译为 OPENJSON，
        // 数据库兼容级别 <130（如 SQL Server 2012）不支持，导入路径承诺兼容旧版 SQL Server。
        var existing = (await dbContext.IcdTermSynonyms
                .Where(item => EF.Constant(termTypes).Contains(item.TermType) && EF.Constant(variants).Contains(item.VariantTerm))
                .ToListAsync(cancellationToken))
            .ToDictionary(
                item => (item.TermType, item.VariantTerm),
                ValueTupleComparer.Instance);

        foreach (var pair in pairs)
        {
            if (existing.TryGetValue((pair.TermType, pair.Variant), out var record))
            {
                record.CanonicalTerm = pair.Canonical;
                record.IsEnabled = true;
                record.UpdatedAt = now;
                continue;
            }

            dbContext.IcdTermSynonyms.Add(new IcdTermSynonymRecord
            {
                Id = Guid.NewGuid(),
                TermType = pair.TermType,
                CanonicalTerm = pair.Canonical,
                VariantTerm = pair.Variant,
                IsEnabled = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
    }

    /// <summary>导入的 entityType 必须能落到诊断或手术通道，两通道改写互不串用。</summary>
    private static string ToTermChannel(string entityType) => entityType.ToUpperInvariant() switch
    {
        "DIAGNOSIS" or "DISEASE" => TermSynonymTypes.Diagnosis,
        "PROCEDURE" or "OPERATION" or "SURGERY" => TermSynonymTypes.Procedure,
        _ => throw new ValidationException(
            $"entityType「{entityType}」无法映射到索引链路通道，仅支持 DISEASE/DIAGNOSIS（诊断）或 PROCEDURE/OPERATION（手术）。"),
    };

    private sealed class ValueTupleComparer : IEqualityComparer<(string TermType, string Term)>
    {
        public static ValueTupleComparer Instance { get; } = new();

        public bool Equals((string TermType, string Term) x, (string TermType, string Term) y) =>
            StringComparer.OrdinalIgnoreCase.Equals(x.TermType, y.TermType)
            && StringComparer.OrdinalIgnoreCase.Equals(x.Term, y.Term);

        public int GetHashCode((string TermType, string Term) obj) =>
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.TermType)
            ^ StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Term);
    }

    private void EnsureHospital()
    {
        if (requestContext.HospitalId == Guid.Empty)
        {
            throw new ValidationException("X-Hospital-Id 不能为空。");
        }
    }

    /// <summary>
    /// 规则 JSON 只做结构合法性校验：条件与动作必须能被 JsonDocument 解析成对象，
    /// 禁止动态执行任意表达式或脚本，评估走定型后的规则求值器。
    /// </summary>
    private static void ValidateRuleJson(string json, string fieldName)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ValidationException($"{fieldName} 必须是 JSON 对象。");
            }
        }
        catch (JsonException exception)
        {
            throw new ValidationException($"{fieldName} 不是合法 JSON 对象：{exception.Message}");
        }
    }

    private sealed class SynonymKeyComparer :
        IEqualityComparer<(string Term, string EntityType, string CodeSystemCode, string Code)>
    {
        public static SynonymKeyComparer Instance { get; } = new();

        public bool Equals(
            (string Term, string EntityType, string CodeSystemCode, string Code) x,
            (string Term, string EntityType, string CodeSystemCode, string Code) y) =>
            StringComparer.OrdinalIgnoreCase.Equals(x.Term, y.Term)
            && StringComparer.OrdinalIgnoreCase.Equals(x.EntityType, y.EntityType)
            && StringComparer.OrdinalIgnoreCase.Equals(x.CodeSystemCode, y.CodeSystemCode)
            && StringComparer.OrdinalIgnoreCase.Equals(x.Code, y.Code);

        public int GetHashCode(
            (string Term, string EntityType, string CodeSystemCode, string Code) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Term),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.EntityType),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.CodeSystemCode),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Code));
    }
}
