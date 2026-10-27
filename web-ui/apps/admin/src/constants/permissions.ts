/**
 * 权限目录树：菜单（一级）→ 功能（二级）→ 按钮（叶子）。
 * 纯分组节点没有 code；带 code 的节点勾选时连同全部后代一起授予。
 * 后端只认扁平权限码，层级仅用于配置与展示。
 */
export interface PermissionNode {
  key: string
  labelKey: string
  descKey?: string
  code?: string
  children?: PermissionNode[]
}

export const PERMISSION_TREE: PermissionNode[] = [
  {
    key: 'menu-coding',
    labelKey: 'account.treeCoding',
    descKey: 'account.treeCodingDesc',
    children: [
      {
        key: 'coding-read',
        labelKey: 'account.permCodingRead',
        descKey: 'account.permCodingReadDesc',
        code: 'coding.read',
      },
      {
        key: 'coding-review',
        labelKey: 'account.permCodingReview',
        descKey: 'account.permCodingReviewDesc',
        code: 'coding.review',
        children: [
          {
            key: 'coding-review-accept',
            labelKey: 'account.permReviewAccept',
            descKey: 'account.permReviewAcceptDesc',
            code: 'coding.review.accept',
          },
          {
            key: 'coding-review-modify',
            labelKey: 'account.permReviewModify',
            descKey: 'account.permReviewModifyDesc',
            code: 'coding.review.modify',
          },
          {
            key: 'coding-review-reject',
            labelKey: 'account.permReviewReject',
            descKey: 'account.permReviewRejectDesc',
            code: 'coding.review.reject',
          },
        ],
      },
    ],
  },
  {
    key: 'menu-case-entry',
    labelKey: 'account.treeCaseEntry',
    descKey: 'account.treeCaseEntryDesc',
    code: 'coding.caseEntry',
  },
  {
    key: 'menu-knowledge',
    labelKey: 'account.treeKnowledge',
    descKey: 'account.treeKnowledgeDesc',
    children: [
      {
        key: 'knowledge-read',
        labelKey: 'account.permKnowledgeRead',
        descKey: 'account.permKnowledgeReadDesc',
        code: 'knowledge.read',
      },
      {
        key: 'knowledge-codesystem-import',
        labelKey: 'account.permCodeSystemImport',
        descKey: 'account.permCodeSystemImportDesc',
        code: 'knowledge.codesystem.import',
      },
      {
        key: 'knowledge-rules-import',
        labelKey: 'account.permRulesImport',
        descKey: 'account.permRulesImportDesc',
        code: 'knowledge.rules.import',
      },
      {
        key: 'knowledge-icd',
        labelKey: 'account.treeIcdIndex',
        descKey: 'account.treeIcdIndexDesc',
        children: [
          { key: 'knowledge-icd-import', labelKey: 'account.permIcdImport', code: 'knowledge.icd.import' },
          { key: 'knowledge-icd-create', labelKey: 'account.permIcdCreate', code: 'knowledge.icd.create' },
          { key: 'knowledge-icd-update', labelKey: 'account.permIcdUpdate', code: 'knowledge.icd.update' },
          { key: 'knowledge-icd-delete', labelKey: 'account.permIcdDelete', code: 'knowledge.icd.delete' },
        ],
      },
      {
        key: 'knowledge-cm3',
        labelKey: 'account.treeCm3Index',
        descKey: 'account.treeCm3IndexDesc',
        children: [
          { key: 'knowledge-cm3-import', labelKey: 'account.permCm3Import', code: 'knowledge.cm3.import' },
          { key: 'knowledge-cm3-create', labelKey: 'account.permCm3Create', code: 'knowledge.cm3.create' },
          { key: 'knowledge-cm3-update', labelKey: 'account.permCm3Update', code: 'knowledge.cm3.update' },
          { key: 'knowledge-cm3-delete', labelKey: 'account.permCm3Delete', code: 'knowledge.cm3.delete' },
        ],
      },
      {
        key: 'knowledge-tumor',
        labelKey: 'account.treeTumorIndex',
        descKey: 'account.treeTumorIndexDesc',
        children: [
          { key: 'knowledge-tumor-create', labelKey: 'account.permTumorCreate', code: 'knowledge.tumor.create' },
          { key: 'knowledge-tumor-update', labelKey: 'account.permTumorUpdate', code: 'knowledge.tumor.update' },
          { key: 'knowledge-tumor-delete', labelKey: 'account.permTumorDelete', code: 'knowledge.tumor.delete' },
        ],
      },
      {
        key: 'knowledge-synonym',
        labelKey: 'account.treeSynonym',
        descKey: 'account.treeSynonymDesc',
        children: [
          { key: 'knowledge-synonym-import', labelKey: 'account.permSynonymImport', code: 'knowledge.synonym.import' },
          { key: 'knowledge-synonym-create', labelKey: 'account.permSynonymCreate', code: 'knowledge.synonym.create' },
          { key: 'knowledge-synonym-update', labelKey: 'account.permSynonymUpdate', code: 'knowledge.synonym.update' },
          { key: 'knowledge-synonym-delete', labelKey: 'account.permSynonymDelete', code: 'knowledge.synonym.delete' },
        ],
      },
      {
        key: 'knowledge-write',
        labelKey: 'account.permKnowledgeWrite',
        descKey: 'account.permKnowledgeWriteDesc',
        code: 'knowledge.write',
        children: [
          {
            key: 'knowledge-hospital-create',
            labelKey: 'account.permHospitalCreate',
            descKey: 'account.permHospitalCreateDesc',
            code: 'knowledge.hospital.create',
          },
          {
            key: 'knowledge-index-rebuild',
            labelKey: 'account.permIndexRebuild',
            descKey: 'account.permIndexRebuildDesc',
            code: 'knowledge.index.rebuild',
          },
        ],
      },
    ],
  },
  {
    key: 'menu-account',
    labelKey: 'account.treeAccount',
    descKey: 'account.treeAccountDesc',
    children: [
      {
        key: 'user-manage',
        labelKey: 'account.permUserManage',
        descKey: 'account.permUserManageDesc',
        code: 'user.manage',
        children: [
          { key: 'user-create', labelKey: 'account.permUserCreate', code: 'user.create' },
          { key: 'user-status', labelKey: 'account.permUserStatus', code: 'user.status' },
          { key: 'user-role-assign', labelKey: 'account.permUserRoleAssign', code: 'user.role.assign' },
          { key: 'role-create', labelKey: 'account.permRoleCreate', code: 'role.create' },
          { key: 'role-permission', labelKey: 'account.permRolePermission', code: 'role.permission' },
        ],
      },
    ],
  },
]

/** 收集节点自身与全部后代的权限码。 */
export function collectCodes(node: PermissionNode): string[] {
  const codes: string[] = []
  if (node.code) codes.push(node.code)
  for (const child of node.children ?? []) codes.push(...collectCodes(child))
  return codes
}

export const ALL_PERMISSION_CODES = PERMISSION_TREE.flatMap(collectCodes)

const nodeByKey = new Map<string, PermissionNode>()
function indexNode(node: PermissionNode) {
  nodeByKey.set(node.key, node)
  for (const child of node.children ?? []) indexNode(child)
}
PERMISSION_TREE.forEach(indexNode)

export function permissionCodesOf(menuKey: string): string[] {
  const node = nodeByKey.get(menuKey)
  return node ? collectCodes(node) : []
}

/** 编码工作台路由：查询、审核或任一审核按钮。 */
export const WORKBENCH_PERMISSIONS = permissionCodesOf('menu-coding')

/** 病例录入菜单。 */
export const CASE_ENTRY_PERMISSIONS = ['coding.caseEntry']

/** 知识库路由：数据浏览或任一知识库按钮。 */
export const KNOWLEDGE_PERMISSIONS = permissionCodesOf('menu-knowledge')

/** 用户与角色管理：功能权限或任一账户按钮。 */
export const ACCOUNT_MANAGE_PERMISSIONS = permissionCodesOf('menu-account')

/** 角色卡分组统计：编码（含病例录入）、知识库、账户。 */
export const PERMISSION_SUMMARY_GROUPS = [
  {
    key: 'coding',
    labelKey: 'account.roleSummaryCoding',
    codes: [...permissionCodesOf('menu-coding'), ...CASE_ENTRY_PERMISSIONS],
  },
  {
    key: 'knowledge',
    labelKey: 'account.roleSummaryKnowledge',
    codes: KNOWLEDGE_PERMISSIONS,
  },
  {
    key: 'account',
    labelKey: 'account.roleSummaryAccount',
    codes: ACCOUNT_MANAGE_PERMISSIONS,
  },
]
