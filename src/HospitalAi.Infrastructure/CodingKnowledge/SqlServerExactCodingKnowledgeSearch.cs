using HospitalAi.Application.Abstractions;
using HospitalAi.Infrastructure.SqlServer;
using Microsoft.EntityFrameworkCore;

namespace HospitalAi.Infrastructure.CodingKnowledge;

/// <summary>
/// SQL Server Exact 检索实现。同义词与标准术语精确相等才命中，
/// 命中分按 同义词 > 标题 > 检索文本 递减，便于评分与 Trace 解释。
/// </summary>
public sealed class SqlServerExactCodingKnowledgeSearch(
    HospitalAiDbContext dbContext) : IExactCodingKnowledgeSearch
{
    public async Task<IReadOnlyList<CodingKnowledgeHit>> SearchAsync(
        ExactCodingKnowledgeQuery query,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(query.QueryText);
        if (string.IsNullOrEmpty(normalized))
        {
            return [];
        }

        var hits = new Dictionary<string, CodingKnowledgeHit>(StringComparer.OrdinalIgnoreCase);

        // 1. 同义词命中（术语归一化结果）。同样先按医院范围预筛再做标准化相等比较。
        var trimmed = query.QueryText.Trim();
        var synonymHits = await dbContext.TermSynonyms
            .AsNoTracking()
            .Where(item => item.HospitalId == query.HospitalId
                && (item.Term.Contains(trimmed) || item.NormalizedTerm.Contains(trimmed)))
            .Take(query.MaxResults * 10)
            .Select(item => new { item.CodeSystemCode, item.Code, item.NormalizedTerm, item.Term })
            .ToListAsync(cancellationToken);

        foreach (var item in synonymHits)
        {
            var key = $"{item.CodeSystemCode}:{item.Code}";
            var matched = Normalize(item.NormalizedTerm) == normalized
                ? item.NormalizedTerm
                : Normalize(item.Term) == normalized
                    ? item.Term
                    : null;
            if (matched is not null && !hits.ContainsKey(key))
            {
                hits[key] = new CodingKnowledgeHit(
                    item.CodeSystemCode,
                    item.Code,
                    matched,
                    CodeType: "SYNONYM",
                    Score: 1.0,
                    MatchedText: matched,
                    RecallSource: "EXACT");
            }
        }

        // 2. 标题/检索文本精确命中。编码表未存规范化键，先按医院范围做子串预筛，
        //    再在内存中做标准化后的严格相等比较，保证“精确命中”语义。
        var codeCandidates = await dbContext.MedicalCodes
            .AsNoTracking()
            .Where(item => item.HospitalId == query.HospitalId
                && item.IsEnabled
                && item.SearchText.Contains(query.QueryText.Trim()))
            .Take(query.MaxResults * 10)
            .Select(item => new
            {
                item.CodeSystemCode,
                item.Code,
                item.Title,
                item.CodeType,
                item.SearchText
            })
            .ToListAsync(cancellationToken);

        foreach (var item in codeCandidates)
        {
            var normalizedTitle = Normalize(item.Title);
            var normalizedSearchText = Normalize(item.SearchText);
            var key = $"{item.CodeSystemCode}:{item.Code}";
            if (normalizedTitle == normalized)
            {
                if (!hits.TryGetValue(key, out var existing) || existing.Score < 0.9)
                {
                    hits[key] = new CodingKnowledgeHit(
                        item.CodeSystemCode, item.Code, item.Title, item.CodeType,
                        0.9, item.Title, "EXACT");
                }
            }
            else if (normalizedSearchText == normalized
                && (!hits.TryGetValue(key, out var existing2) || existing2.Score < 0.8))
            {
                hits[key] = new CodingKnowledgeHit(
                    item.CodeSystemCode, item.Code, item.Title, item.CodeType,
                    0.8, item.SearchText, "EXACT");
            }
        }

        return hits.Values
            .OrderByDescending(item => item.Score)
            .Take(query.MaxResults)
            .ToList();
    }

    private static string Normalize(string text)
    {
        return new string(text
            .Trim()
            .Where(character => !char.IsPunctuation(character))
            .ToArray())
            .Replace(" ", string.Empty)
            .ToLowerInvariant();
    }
}
