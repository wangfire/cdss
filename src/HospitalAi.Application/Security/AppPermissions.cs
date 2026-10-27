namespace HospitalAi.Application.Security;

/// <summary>
/// 平台内置权限码：菜单/功能权限 + 按钮级权限。
/// 角色可配置任意子集；ADMIN 角色固定拥有全部权限，不落库。
/// 层级（菜单 → 功能 → 按钮）只用于前端权限树展示，后端按扁平码判定；
/// 前端授予按钮权限时会一并带上其祖先功能码，保证页面入口与按钮权限一致。
/// </summary>
public static class AppPermissions
{
    // 编码工作台
    public const string CodingRead = "coding.read";

    public const string CodingReview = "coding.review";

    public const string CodingReviewAccept = "coding.review.accept";

    public const string CodingReviewModify = "coding.review.modify";

    public const string CodingReviewReject = "coding.review.reject";

    public const string CodingCaseEntry = "coding.caseEntry";

    // 知识库管理
    public const string KnowledgeRead = "knowledge.read";

    public const string KnowledgeWrite = "knowledge.write";

    public const string KnowledgeHospitalCreate = "knowledge.hospital.create";

    public const string KnowledgeIndexRebuild = "knowledge.index.rebuild";

    public const string KnowledgeCodeSystemImport = "knowledge.codesystem.import";

    public const string KnowledgeRulesImport = "knowledge.rules.import";

    public const string KnowledgeIcdImport = "knowledge.icd.import";

    public const string KnowledgeIcdCreate = "knowledge.icd.create";

    public const string KnowledgeIcdUpdate = "knowledge.icd.update";

    public const string KnowledgeIcdDelete = "knowledge.icd.delete";

    public const string KnowledgeCm3Import = "knowledge.cm3.import";

    public const string KnowledgeCm3Create = "knowledge.cm3.create";

    public const string KnowledgeCm3Update = "knowledge.cm3.update";

    public const string KnowledgeCm3Delete = "knowledge.cm3.delete";

    public const string KnowledgeTumorCreate = "knowledge.tumor.create";

    public const string KnowledgeTumorUpdate = "knowledge.tumor.update";

    public const string KnowledgeTumorDelete = "knowledge.tumor.delete";

    public const string KnowledgeSynonymImport = "knowledge.synonym.import";

    public const string KnowledgeSynonymCreate = "knowledge.synonym.create";

    public const string KnowledgeSynonymUpdate = "knowledge.synonym.update";

    public const string KnowledgeSynonymDelete = "knowledge.synonym.delete";

    // 用户与角色管理
    public const string UserManage = "user.manage";

    public const string UserCreate = "user.create";

    public const string UserStatus = "user.status";

    public const string UserRoleAssign = "user.role.assign";

    public const string RoleCreate = "role.create";

    public const string RolePermission = "role.permission";

    public static readonly IReadOnlyList<string> All =
    [
        CodingRead,
        CodingReview,
        CodingReviewAccept,
        CodingReviewModify,
        CodingReviewReject,
        CodingCaseEntry,
        KnowledgeRead,
        KnowledgeWrite,
        KnowledgeHospitalCreate,
        KnowledgeIndexRebuild,
        KnowledgeCodeSystemImport,
        KnowledgeRulesImport,
        KnowledgeIcdImport,
        KnowledgeIcdCreate,
        KnowledgeIcdUpdate,
        KnowledgeIcdDelete,
        KnowledgeCm3Import,
        KnowledgeCm3Create,
        KnowledgeCm3Update,
        KnowledgeCm3Delete,
        KnowledgeTumorCreate,
        KnowledgeTumorUpdate,
        KnowledgeTumorDelete,
        KnowledgeSynonymImport,
        KnowledgeSynonymCreate,
        KnowledgeSynonymUpdate,
        KnowledgeSynonymDelete,
        UserManage,
        UserCreate,
        UserStatus,
        UserRoleAssign,
        RoleCreate,
        RolePermission
    ];

    public static bool IsDefined(string code)
    {
        return All.Contains(code, StringComparer.Ordinal);
    }

    /// <summary>按目录顺序去重，保证令牌与接口返回稳定。</summary>
    public static IReadOnlyList<string> Order(IEnumerable<string> codes)
    {
        var set = codes.ToHashSet(StringComparer.Ordinal);
        return All.Where(set.Contains).ToList();
    }
}
