/**
 * 中文知识库翻译
 */
export default {
  // 页面标题
  title: '知识库管理',
  subtitle: '管理 ICD 编码字典、同义词和编码规则',

  // 编码体系导入
  codeSystemImport: '编码体系导入',
  codeSystemImportDesc: '导入 ICD-10 疾病编码或 ICD-9-CM-3 手术编码字典',
  codeSystem: '编码体系',
  version: '版本',
  icd10: 'ICD-10 疾病编码',
  icd9cm3: 'ICD-9-CM-3 手术编码',
  importCodes: '导入编码',
  importedCount: '新增编码',
  updatedCount: '更新编码',
  importSuccess: '编码体系导入成功',
  importFailed: '编码体系导入失败',

  // 规则导入
  rulesImport: '规则与同义词导入',
  rulesImportDesc: '导入医学术语同义词和编码校验规则',
  synonyms: '同义词',
  rules: '编码规则',
  importRules: '导入规则',
  synonymCount: '同义词数',
  ruleCount: '规则数',
  rulesImportSuccess: '规则导入成功',
  rulesImportFailed: '规则导入失败',

  // 知识库统计
  knowledgeStats: '知识库统计',
  totalCodes: '编码总数',
  enabledCodes: '已启用',
  totalSynonyms: '同义词数',
  totalRules: '规则数',

  // 表单
  codeSystemPlaceholder: '请选择编码体系',
  versionPlaceholder: '请输入版本号',
  fileUpload: '上传文件',
  fileUploadDesc: '支持 JSON 格式文件',
  selectFile: '选择文件',
  noFileSelected: '未选择文件',

  // 示例数据
  loadSampleData: '加载示例数据',
  sampleDataLoaded: '示例数据已加载',

  // 数据浏览
  browseData: '数据浏览',
  codeSystems: '编码体系',
  medicalCodes: '医学编码',
  termSynonyms: '术语同义词',
  codingRulesTab: '编码规则',

  // 编码体系浏览
  codeColumn: '编码',
  nameColumn: '名称',
  versionColumn: '版本',
  createdAtColumn: '创建时间',
  noCodeSystems: '暂无编码体系，请先导入',

  // 医学编码浏览
  codeTitle: '编码名称',
  codeSystemColumn: '所属体系',
  searchTextColumn: '检索词',
  statusColumn: '状态',
  enabled: '已启用',
  disabled: '已禁用',
  searchPlaceholder: '搜索编码、名称或检索词',
  filterByCodeSystem: '按编码体系筛选',
  allCodeSystems: '全部体系',
  totalRecords: '共 {total} 条',

  // 同义词浏览
  termColumn: '术语',
  normalizedTermColumn: '标准名称',
  entityTypeColumn: '实体类型',
  relatedCodeColumn: '关联编码',
  searchSynonymPlaceholder: '搜索术语或标准名称',

  // 规则浏览
  ruleCodeColumn: '规则编码',
  codePatternColumn: '编码模式',
  ruleTypeColumn: '规则类型',
  severityColumn: '严重级别',
  messageColumn: '提示信息',
  filterByRuleCodeSystem: '按体系筛选',

  // 分页
  pagination: '第 {page} / {totalPages} 页',
  previousPage: '上一页',
  nextPage: '下一页',

  // 加载状态
  loading: '加载中...',
  loadFailed: '数据加载失败',
}
