namespace HospitalAi.Application.Security;

/// <summary>
/// 正式权限矩阵：内置角色模板及其授权口径（平台唯一权威定义）。
/// - ADMIN 固定全权（不落库，登录时动态展开）；
/// - 模板角色供「创建角色时一键套用」与存量环境权限矩阵种子（SeedRoleMatrix 迁移）复用；
/// - 迁移内 SQL 与本定义保持同口径，新增权限码时两处同步维护；
/// - 医院定制角色的权限集完全由管理端（role.permission 写权）收敛。
/// </summary>
public static class RoleMatrix
{
    public sealed record RoleTemplate(
        string Code,
        string Name,
        string Description,
        IReadOnlyList<string> Permissions);

    /// <summary>编码员：编码工作台读写与全量审核按钮 + 病例录入。</summary>
    public static readonly RoleTemplate Coder = new(
        "CODER",
        "编码员",
        "编码工作台读写与全量审核，含病例录入。",
        [
            AppPermissions.CodingRead,
            AppPermissions.CodingReview,
            AppPermissions.CodingReviewAccept,
            AppPermissions.CodingReviewModify,
            AppPermissions.CodingReviewReject,
            AppPermissions.CodingCaseEntry
        ]);

    /// <summary>临床医生：只读 + 病例录入，不做审核。</summary>
    public static readonly RoleTemplate Doctor = new(
        "DOCTOR",
        "临床医生",
        "查看编码结果与病例录入，不做审核。",
        [
            AppPermissions.CodingRead,
            AppPermissions.CodingCaseEntry
        ]);

    /// <summary>病案审核：审核功能（接受/修改/拒绝），不含病例录入。</summary>
    public static readonly RoleTemplate Reviewer = new(
        "REVIEWER",
        "病案审核",
        "编码结果审核，不含病例录入。",
        [
            AppPermissions.CodingRead,
            AppPermissions.CodingReview
        ]);

    /// <summary>知识库管理：知识库读写与全部维护按钮。</summary>
    public static readonly RoleTemplate KnowledgeManager = new(
        "KNOWLEDGE_MANAGER",
        "知识库管理员",
        "知识库浏览与全量维护（索引/字典/规则/同义词）。",
        [
            AppPermissions.KnowledgeRead,
            AppPermissions.KnowledgeWrite,
            AppPermissions.KnowledgeHospitalCreate,
            AppPermissions.KnowledgeIndexRebuild,
            AppPermissions.KnowledgeCodeSystemImport,
            AppPermissions.KnowledgeRulesImport,
            AppPermissions.KnowledgeIcdImport,
            AppPermissions.KnowledgeIcdCreate,
            AppPermissions.KnowledgeIcdUpdate,
            AppPermissions.KnowledgeIcdDelete,
            AppPermissions.KnowledgeCm3Import,
            AppPermissions.KnowledgeCm3Create,
            AppPermissions.KnowledgeCm3Update,
            AppPermissions.KnowledgeCm3Delete,
            AppPermissions.KnowledgeTumorCreate,
            AppPermissions.KnowledgeTumorUpdate,
            AppPermissions.KnowledgeTumorDelete,
            AppPermissions.KnowledgeSynonymImport,
            AppPermissions.KnowledgeSynonymCreate,
            AppPermissions.KnowledgeSynonymUpdate,
            AppPermissions.KnowledgeSynonymDelete
        ]);

    /// <summary>
    /// 账户管理员：用户与角色管理。
    /// 刻意不含 role.permission 写权：避免「只授账户管理」的角色自行改写角色权限形成提权闭环。
    /// </summary>
    public static readonly RoleTemplate UserAdmin = new(
        "USER_ADMIN",
        "账户管理员",
        "用户与角色管理；角色权限调整需管理员另行授予。",
        [
            AppPermissions.UserManage,
            AppPermissions.UserCreate,
            AppPermissions.UserStatus,
            AppPermissions.UserRoleAssign,
            AppPermissions.RoleCreate
        ]);

    /// <summary>内置模板全集（按授权面排序展示）。</summary>
    public static IReadOnlyList<RoleTemplate> BuiltIn { get; } =
    [
        Coder,
        Doctor,
        Reviewer,
        KnowledgeManager,
        UserAdmin
    ];

    /// <summary>按模板码取模板；未定义返回 null。</summary>
    public static RoleTemplate? Find(string code)
    {
        foreach (var template in BuiltIn)
        {
            if (string.Equals(template.Code, code, StringComparison.OrdinalIgnoreCase))
            {
                return template;
            }
        }

        return null;
    }
}
