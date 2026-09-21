/**
 * 中文工作台翻译
 */
export default {
  // 页面标题
  title: '编码工作台',
  subtitle: '管理编码任务、查看 AI 推荐、进行人工审核',

  // 状态筛选
  filterAll: '全部',
  filterPending: '待处理',
  filterRunning: '运行中',
  filterHumanRequired: '需人工审核',
  filterPendingReview: '待审核',
  filterAccepted: '已接受',
  filterModified: '已修改',
  filterRejected: '已拒绝',
  filterFailed: '失败',

  // 任务列表
  taskId: '任务 ID',
  visitId: '就诊 ID',
  status: '状态',
  recommendations: '推荐数',
  createdAt: '创建时间',
  actions: '操作',
  viewDetails: '查看详情',
  noTasks: '暂无编码任务',
  noTasksDesc: '创建就诊和文档后，系统将自动生成编码任务',

  // 任务详情
  taskDetail: '任务详情',
  recommendationsTitle: 'AI 编码推荐',
  noRecommendations: '暂无推荐结果',
  noRecommendationsDesc: '任务正在处理中或尚未生成推荐',

  // 推荐项
  codeSystem: '编码体系',
  code: '编码',
  codeTitle: '名称',
  confidence: '置信度',
  rank: '排名',
  type: '类型',
  reviewStatus: '审核状态',
  evidence: '证据链',
  sourceType: '来源类型',
  sourceText: '来源文本',
  matchText: '匹配文本',
  score: '得分',

  // 审核操作
  review: '人工审核',
  acceptAll: '全部接受',
  rejectAll: '全部拒绝',
  submitReview: '提交审核',
  reviewComment: '审核备注',
  reviewCommentPlaceholder: '请输入审核备注（可选）',
  selectCodes: '选择',
  reviewSuccess: '审核提交成功',
  reviewFailed: '审核提交失败',
  confirmAccept: '确认接受所有推荐编码？',
  confirmReject: '确认拒绝所有推荐编码？',

  // 状态标签
  statusPending: '待处理',
  statusRunning: '运行中',
  statusSuccess: '成功',
  statusFailed: '失败',
  statusRetrying: '重试中',
  statusTimeout: '超时',
  statusCancelled: '已取消',
  statusHumanRequired: '需人工',
  statusPendingReview: '待审核',
  statusAccepted: '已接受',
  statusModified: '已修改',
  statusRejected: '已拒绝',
}
