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
  totalSynonyms: '同义词组数',
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
  icdIndexTab: 'ICD索引库',

  // ICD 索引库浏览
  seqNoColumn: '序号',
  rawNameColumn: '索引原始名称',
  rawNameEnColumn: '索引原始英文名称',
  indexNameColumn: '索引名称',
  indexCodeColumn: '索引编码',
  indexLevelColumn: '索引级别',
  parentNodeColumn: '父节点',
  fullIndexNameColumn: '完整索引名称',
  leadWordStatusColumn: '主导词状态',
  leadWordValid: '有效',
  leadWordInvalid: '无效',
  searchIcdIndexPlaceholder: '搜索索引名称、完整名称或编码',
  filterByLeadWordStatus: '按主导词状态筛选',
  allLeadWordStatus: '全部状态',
  noIcdIndex: '暂无数据',
  expandNode: '展开子节点',
  collapseNode: '收起子节点',
  icdIndexRootProgress: '顶级索引已加载 {loaded} / {total}',
  loadMoreRoots: '加载更多顶级索引',

  // CM3（手术操作）索引库浏览
  cm3IndexTab: 'CM3索引库',
  searchCm3IndexPlaceholder: '搜索手术索引名称、完整名称或编码',
  noCm3Index: '暂无数据',
  cm3IndexRootProgress: '顶级手术索引已加载 {loaded} / {total}',

  // 肿瘤索引库浏览
  tumorIndexTab: '肿瘤索引库',
  searchTumorIndexPlaceholder: '搜索肿瘤部位词条、完整路径或形态学码',
  noTumorIndex: '暂无数据',
  tumorIndexRootProgress: '顶级肿瘤词条已加载 {loaded} / {total}',
  codeMalignantPrimaryColumn: '恶性原发',
  codeMalignantSecondaryColumn: '恶性继发',
  codeInSituColumn: '原位',
  codeBenignColumn: '良性',
  codeUncertainColumn: '动态未定',
  jumpKindColumn: '跳转',
  jumpKindNone: '—',
  jumpKindSee: '见',
  jumpKindSeeAlso: '另见',

  // 索引库编辑（ICD/CM3/肿瘤共用弹窗）
  addRootIndexEntry: '新增顶级',
  addChildIndexEntry: '新增子级',
  editIndexEntry: '编辑索引行',
  createIndexEntry: '新增索引行',
  createChildIndexEntry: '新增子级索引行',
  indexNameEnField: '英文名称',
  tumorCodesLabel: '五个动态码（恶性原发/继发、原位、良性、动态未定）',
  jumpTargetField: '跳转目标',
  jumpKindNoneOption: '无跳转',
  fullIndexNamePlaceholder: '留空则默认为索引名称',
  parentSeqNoHint: '父节点序号 0 表示顶级，其余必须是库中已存在的行',
  seqNoLockedHint: '序号创建后不可修改（子节点按序号挂父链）',
  indexNamePlaceholder: '如 肿瘤，肿瘤性',
  indexEntrySaved: '索引行已保存',
  indexEntryDeleted: '索引行已删除',
  deleteIndexEntryConfirm: '确认删除「{name}」（序号 {seqNo}）？',
  seqNoRequired: '请填写正整数序号',
  indexNameRequired: '请填写索引名称',
  indexLevelRange: '索引级别只能是 0~9',
  parentSeqNoInvalid: '父节点序号不能为负数',
  jumpTargetRequired: '跳转类型为「见」或「另见」时必须填写目标词',

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

  // 同义词浏览（表 icd_term_synonym）
  variantTermsColumn: '同义词（变体）',
  normalizedTermColumn: '规范词',
  entityTypeColumn: '通道类型',
  termTypeDiagnosis: '诊断',
  termTypeProcedure: '手术',
  searchSynonymPlaceholder: '搜索同义词或规范词',
  equivalenceGroupColumn: '等同词组',
  equivalenceHint: '一组内的任意两个词都可互相等同（含规范词），一行就是一条等同关系。',
  createSynonymGroup: '新增同义词组',
  editSynonymGroup: '编辑同义词组',
  canonicalTermPlaceholder: '规范词，如 期前收缩',
  variantTermPlaceholder: '等同词，如 早搏',
  addVariantTerm: '添加等同词',
  actionsColumn: '操作',
  edit: '编辑',
  delete: '删除',
  save: '保存',
  synonymSaved: '同义词已保存',
  synonymDeleted: '同义词组已删除',
  saveFailed: '保存失败',
  deleteFailed: '删除失败',
  deleteGroupConfirm: '确认删除「{canonical}」这一组等同词（共 {count} 条）？',
  canonicalTermRequired: '请填写规范词',
  atLeastOneVariantRequired: '至少填写一个等同词',
  variantEqualsCanonical: '等同词不能与规范词相同',
  duplicateVariantTerm: '等同词不能重复',

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
