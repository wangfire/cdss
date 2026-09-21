/**
 * 中文病例录入翻译
 */
export default {
  title: '病例录入',
  subtitle: '录入单条病案基本信息与诊断 / 手术，确认后即刻运行 AI 推荐编码',

  basicInfo: '基本信息',
  patientName: '姓名',
  patientNamePlaceholder: '患者姓名',
  medicalRecordNo: '病案号',
  medicalRecordNoPlaceholder: '病案号（院内唯一）',
  admissionCount: '住院次数',
  admissionCountPlaceholder: '留空自动累计',
  admissionAt: '入院日期',
  dischargeAt: '出院日期',

  clinical: '诊断与手术',
  onePerLine: '每行一条',
  admissionDiagnoses: '入院诊断',
  dischargeDiagnoses: '出院诊断',
  principalHint: '每行一条，首条自动作为主诊断',
  procedures: '手术操作',
  additionalDocument: '附加文书正文（可选）',
  additionalDocumentPlaceholder: '入院记录等自由文本，作为补充证据进入流水线…',

  submit: '确认录入并 AI 推荐',
  submitting: '流水线执行中，请稍候…',
  submitTimeoutHint: '执行时间较长（含检索与评分），请勿关闭页面',

  successTitle: '录入完成',
  inputCount: '诊断输入',
  recCount: 'AI 推荐',
  visitCount: '住院次数',
  pipeline: '流水线',
  inputsLabel: '生成的诊断输入',
  goWorkbench: '前往工作台审核',
  another: '继续录入下一条',

  recPanelTitle: 'AI 推荐编码',
  recPanelEmpty: '填写左侧与中间信息并提交，推荐编码将直接显示在这里',
  recPanelRunning: '流水线执行中，完成后自动展示推荐…',
  recPanelLoading: '正在加载推荐结果…',
  recPanelNone: '本次未产生推荐编码',
  recFetchFailed: '推荐结果加载失败，可前往工作台查看',
  principal: '主诊断',

  nameRequired: '请填写姓名',
  recordNoRequired: '请填写病案号',
  admissionDateRequired: '请选择入院日期',
  dischargeRequired: '至少录入一条出院诊断',
  submitFailed: '录入失败',
}
