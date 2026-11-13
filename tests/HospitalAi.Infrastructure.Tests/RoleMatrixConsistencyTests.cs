using System.Text.RegularExpressions;

namespace HospitalAi.Infrastructure.Tests;

/// <summary>
/// 权限矩阵双写一致性：RoleMatrix.BuiltIn（应用层权威定义）与
/// SeedRoleMatrix 迁移内联 SQL 必须是同一份授权口径。
///
/// 双写是历史遗留的刻意取舍（迁移要能在存量库上独立回填，不能反向引用应用层），
/// 注释里明确要求「新增权限码时同步维护」。没有一致性测试时两边迟早漂移 ——
/// 漂移的后果：迁移回填的权限集与应用层模板不符，管理端"套用模板"与存量库权限不一致。
/// 本测试把同步要求变成硬门禁。
/// </summary>
public sealed class RoleMatrixConsistencyTests
{
    /// <summary>RoleMatrix.cs 源文件路径（测试从仓库根解析）。</summary>
    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "HospitalAi.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("无法从测试程序集目录上溯到仓库根。");
    }

    /// <summary>解析 RoleMatrix.BuiltIn：模板码 → 权限码集合。</summary>
    private static Dictionary<string, HashSet<string>> ParseRoleMatrix()
    {
        var path = Path.Combine(RepoRoot(), "src", "HospitalAi.Application", "Security", "RoleMatrix.cs");
        var source = File.ReadAllText(path);

        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        // 形如：new("CODER", "编码员", "...", [ ... 权限常量 ... ])
        var templateRegex = new Regex(
            @"new\(\s*""(?<code>[A-Z_]+)""(?<body>.*?)\]\s*\)",
            RegexOptions.Singleline | RegexOptions.Compiled);
        var permissionRegex = new Regex(
            @"AppPermissions\.(?<name>[A-Za-z]+)",
            RegexOptions.Compiled);

        foreach (Match template in templateRegex.Matches(source))
        {
            var code = template.Groups["code"].Value;
            var body = template.Groups["body"].Value;
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match permission in permissionRegex.Matches(body))
            {
                if (PermissionNameToCode.TryGetValue(permission.Groups["name"].Value, out var permissionCode))
                {
                    set.Add(permissionCode);
                }
            }

            result[code] = set;
        }

        return result;
    }

    /// <summary>解析 SeedRoleMatrix 迁移 SQL：模板码 → 权限码集合。</summary>
    private static Dictionary<string, HashSet<string>> ParseMigration()
    {
        var path = Path.Combine(
            RepoRoot(), "src", "HospitalAi.Infrastructure", "SqlServer", "Migrations",
            "20261030091000_SeedRoleMatrix.cs");
        var source = File.ReadAllText(path);

        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        // 形如：('CODER', 'coding.read'),
        var pairRegex = new Regex(
            @"\('(?<template>[A-Z_]+)',\s*'(?<permission>[a-zA-Z.]+)'\)",
            RegexOptions.Compiled);

        foreach (Match pair in pairRegex.Matches(source))
        {
            var template = pair.Groups["template"].Value;
            var permission = pair.Groups["permission"].Value;
            if (!result.TryGetValue(template, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                result[template] = set;
            }

            set.Add(permission);
        }

        return result;
    }

    /// <summary>AppPermissions 常量名 → 权限码（与 RoleMatrix.cs 里的引用方式对应）。</summary>
    private static readonly Dictionary<string, string> PermissionNameToCode = new(StringComparer.Ordinal)
    {
        [nameof(HospitalAi.Application.Security.AppPermissions.CodingRead)] = HospitalAi.Application.Security.AppPermissions.CodingRead,
        [nameof(HospitalAi.Application.Security.AppPermissions.CodingReview)] = HospitalAi.Application.Security.AppPermissions.CodingReview,
        [nameof(HospitalAi.Application.Security.AppPermissions.CodingReviewAccept)] = HospitalAi.Application.Security.AppPermissions.CodingReviewAccept,
        [nameof(HospitalAi.Application.Security.AppPermissions.CodingReviewModify)] = HospitalAi.Application.Security.AppPermissions.CodingReviewModify,
        [nameof(HospitalAi.Application.Security.AppPermissions.CodingReviewReject)] = HospitalAi.Application.Security.AppPermissions.CodingReviewReject,
        [nameof(HospitalAi.Application.Security.AppPermissions.CodingCaseEntry)] = HospitalAi.Application.Security.AppPermissions.CodingCaseEntry,
        [nameof(HospitalAi.Application.Security.AppPermissions.KnowledgeRead)] = HospitalAi.Application.Security.AppPermissions.KnowledgeRead,
        [nameof(HospitalAi.Application.Security.AppPermissions.KnowledgeWrite)] = HospitalAi.Application.Security.AppPermissions.KnowledgeWrite,
        [nameof(HospitalAi.Application.Security.AppPermissions.KnowledgeHospitalCreate)] = HospitalAi.Application.Security.AppPermissions.KnowledgeHospitalCreate,
        [nameof(HospitalAi.Application.Security.AppPermissions.KnowledgeIndexRebuild)] = HospitalAi.Application.Security.AppPermissions.KnowledgeIndexRebuild,
        [nameof(HospitalAi.Application.Security.AppPermissions.KnowledgeCodeSystemImport)] = HospitalAi.Application.Security.AppPermissions.KnowledgeCodeSystemImport,
        [nameof(HospitalAi.Application.Security.AppPermissions.KnowledgeRulesImport)] = HospitalAi.Application.Security.AppPermissions.KnowledgeRulesImport,
        [nameof(HospitalAi.Application.Security.AppPermissions.KnowledgeIcdImport)] = HospitalAi.Application.Security.AppPermissions.KnowledgeIcdImport,
        [nameof(HospitalAi.Application.Security.AppPermissions.KnowledgeIcdCreate)] = HospitalAi.Application.Security.AppPermissions.KnowledgeIcdCreate,
        [nameof(HospitalAi.Application.Security.AppPermissions.KnowledgeIcdUpdate)] = HospitalAi.Application.Security.AppPermissions.KnowledgeIcdUpdate,
        [nameof(HospitalAi.Application.Security.AppPermissions.KnowledgeIcdDelete)] = HospitalAi.Application.Security.AppPermissions.KnowledgeIcdDelete,
        [nameof(HospitalAi.Application.Security.AppPermissions.KnowledgeCm3Import)] = HospitalAi.Application.Security.AppPermissions.KnowledgeCm3Import,
        [nameof(HospitalAi.Application.Security.AppPermissions.KnowledgeCm3Create)] = HospitalAi.Application.Security.AppPermissions.KnowledgeCm3Create,
        [nameof(HospitalAi.Application.Security.AppPermissions.KnowledgeCm3Update)] = HospitalAi.Application.Security.AppPermissions.KnowledgeCm3Update,
        [nameof(HospitalAi.Application.Security.AppPermissions.KnowledgeCm3Delete)] = HospitalAi.Application.Security.AppPermissions.KnowledgeCm3Delete,
        [nameof(HospitalAi.Application.Security.AppPermissions.KnowledgeTumorCreate)] = HospitalAi.Application.Security.AppPermissions.KnowledgeTumorCreate,
        [nameof(HospitalAi.Application.Security.AppPermissions.KnowledgeTumorUpdate)] = HospitalAi.Application.Security.AppPermissions.KnowledgeTumorUpdate,
        [nameof(HospitalAi.Application.Security.AppPermissions.KnowledgeTumorDelete)] = HospitalAi.Application.Security.AppPermissions.KnowledgeTumorDelete,
        [nameof(HospitalAi.Application.Security.AppPermissions.KnowledgeSynonymImport)] = HospitalAi.Application.Security.AppPermissions.KnowledgeSynonymImport,
        [nameof(HospitalAi.Application.Security.AppPermissions.KnowledgeSynonymCreate)] = HospitalAi.Application.Security.AppPermissions.KnowledgeSynonymCreate,
        [nameof(HospitalAi.Application.Security.AppPermissions.KnowledgeSynonymUpdate)] = HospitalAi.Application.Security.AppPermissions.KnowledgeSynonymUpdate,
        [nameof(HospitalAi.Application.Security.AppPermissions.KnowledgeSynonymDelete)] = HospitalAi.Application.Security.AppPermissions.KnowledgeSynonymDelete,
        [nameof(HospitalAi.Application.Security.AppPermissions.UserManage)] = HospitalAi.Application.Security.AppPermissions.UserManage,
        [nameof(HospitalAi.Application.Security.AppPermissions.UserCreate)] = HospitalAi.Application.Security.AppPermissions.UserCreate,
        [nameof(HospitalAi.Application.Security.AppPermissions.UserStatus)] = HospitalAi.Application.Security.AppPermissions.UserStatus,
        [nameof(HospitalAi.Application.Security.AppPermissions.UserRoleAssign)] = HospitalAi.Application.Security.AppPermissions.UserRoleAssign,
        [nameof(HospitalAi.Application.Security.AppPermissions.RoleCreate)] = HospitalAi.Application.Security.AppPermissions.RoleCreate,
        [nameof(HospitalAi.Application.Security.AppPermissions.RolePermission)] = HospitalAi.Application.Security.AppPermissions.RolePermission,
    };

    [Fact]
    public void 迁移内联权限矩阵与应用层模板必须一致()
    {
        var fromApp = ParseRoleMatrix();
        var fromMigration = ParseMigration();

        Assert.True(
            fromApp.Count > 0,
            "RoleMatrix.BuiltIn 解析结果为空 —— 解析正则可能因代码结构变化而失效。");
        Assert.True(
            fromMigration.Count > 0,
            "SeedRoleMatrix 迁移解析结果为空 —— 解析正则可能因 SQL 结构变化而失效。");

        var problems = new List<string>();
        foreach (var template in fromApp.Keys.Union(fromMigration.Keys).OrderBy(code => code, StringComparer.Ordinal))
        {
            fromApp.TryGetValue(template, out var appPermissions);
            fromMigration.TryGetValue(template, out var migrationPermissions);
            appPermissions ??= new HashSet<string>(StringComparer.Ordinal);
            migrationPermissions ??= new HashSet<string>(StringComparer.Ordinal);

            foreach (var missing in appPermissions.Except(migrationPermissions).OrderBy(code => code, StringComparer.Ordinal))
            {
                problems.Add($"  {template}: 应用层模板有 '{missing}'，迁移内联缺");
            }

            foreach (var extra in migrationPermissions.Except(appPermissions).OrderBy(code => code, StringComparer.Ordinal))
            {
                problems.Add($"  {template}: 迁移内联有 '{extra}'，应用层模板缺");
            }
        }

        Assert.True(
            problems.Count == 0,
            "RoleMatrix.BuiltIn 与 SeedRoleMatrix 迁移内联 SQL 权限矩阵不一致：\n"
            + string.Join("\n", problems) + "\n"
            + "新增权限码时请同步维护两处（见 RoleMatrix.cs 头注释）。");
    }
}
