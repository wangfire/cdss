/**
 * 中文仪表盘翻译
 */
export default {
  // 页面标题
  dashboard: '仪表盘',
  dashboardWelcome: '欢迎，{name}！今天是 {date}',

  // 核心指标
  metrics: {
    totalTasks: '任务总数',
    pendingReview: '待审核',
    accepted: '已完成',
    processing: '处理中',
    abnormal: '异常',
    systemHealth: '系统健康',
  },

  // 图表
  charts: {
    statusTitle: '任务状态分布',
    trendTitle: '近 7 日任务量',
    trendFallbackTitle: '最近活跃 7 日',
    trendHint: '近 7 日无新增，已按最近活跃日回溯',
    weekTotal: '7 日合计',
    prevWeek: '前 7 日',
    healthyDeps: '{ok}/{total} 依赖正常',
    pendingRate: '占比 {rate}%',
    completionRate: '完成率 {rate}%',
    recommendationTotal: '累计推荐 {count} 条',
    pendingRecs: '待审核推荐',
    avgRecs: '平均每条任务',
    distTitle: '每条任务推荐量分布',
    distTotal: '共 {count} 条任务',
    noTasks: '暂无任务数据',
  },

  // 知识库概况
  knowledge: {
    title: '知识库概况',
    subtitle: '{count} 个编码体系',
    icd: 'ICD-10 诊断索引',
    cm3: 'ICD-9-CM3 手术索引',
    tumor: '肿瘤部位索引',
    synonyms: '术语同义词',
    rules: '编码校验规则',
    medicalCodes: '医学编码条目',
    validLead: '有效主导词 {count}',
    open: '进入知识库',
    empty: '暂无知识库数据',
  },

  // 指标描述
  activeUsersRatio: '活跃用户占比 {ratio}%',
  monthlyTargetComplete: '本月目标完成率 {ratio}%',
  pendingOrders: '待处理订单 {count}',
  aboveIndustry: '高于行业平均 {ratio}%',

  // 本月业绩
  monthlyPerformance: '本月业绩',
  realTimeMonitor: '实时监控业务数据和核心指标',

  // 本周
  thisWeek: '本周',
  thisWeekRevenue: '本周收入',
  thisWeekOrders: '本周订单',
  newCustomers: '新客户',

  // 趋势
  weeklyTrend: '本周趋势',
  weeklyTrendDesc: '过去 7 天数据变化',

  // 快捷入口
  quickEntry: '快捷入口',
  newOrder: '新建订单',
  addUser: '添加用户',
  generateReport: '生成报表',
  systemSettings: '系统设置',

  // 系统状态
  systemStatus: '系统状态',
  operational: '正常运行',
  degraded: '部分降级',
  unhealthy: '异常',
  statusUnknown: '状态未知',
  searchEngine: '检索引擎',
  apiService: 'API 服务',
  database: '数据库',
  messageQueue: '消息队列',

  // 订单状态
  orderStatus: '订单状态',
  newOrders: '新订单',
  processing: '处理中',
  shipping: '配送中',
  completed: '已完成',

  // 待处理事项
  todoItems: '待处理事项',
  inventoryWarning: '库存预警',
  pendingReview: '待审核订单',
  pendingRefund: '待处理退款',
  systemNotification: '系统通知',

  // 活动
  recentActivities: '最近动态',
  success: '成功',
  warning: '警告',
  error: '错误',
  info: '信息',

  // 时间
  minutesAgo: '{count} 分钟前',
  hoursAgo: '{count} 小时前',

  // 星期
  mon: '周一',
  tue: '周二',
  wed: '周三',
  thu: '周四',
  fri: '周五',
  sat: '周六',
  sun: '周日',

  // 查看详情
  viewDetails: '查看详情',

  // 系统状态
  availableRate: '可用率 {uptime}',

  // 处理效率
  avgProcessingTime: '平均处理时间',
  paymentProcessing: '支付处理',
  orderConfirmation: '订单确认',
  shippingProcessing: '发货处理',
  customerResponse: '客户响应',
  seconds: '{count} 秒',
  minutes: '{count} 分钟',
}
