/**
 * English dashboard translations
 */
export default {
  // Page title
  dashboard: 'Dashboard',
  dashboardWelcome: 'Welcome, {name}! Today is {date}',

  // Core metrics
  metrics: {
    totalTasks: 'Total Tasks',
    pendingReview: 'Pending Review',
    accepted: 'Completed',
    processing: 'Processing',
    abnormal: 'Abnormal',
    systemHealth: 'System Health',
  },

  // Charts
  charts: {
    statusTitle: 'Task Status',
    trendTitle: 'Tasks in Last 7 Days',
    trendFallbackTitle: 'Last Active 7 Days',
    trendHint: 'No new tasks in the last 7 days; showing the latest active week',
    weekTotal: '7-day total',
    prevWeek: 'Previous 7 days',
    healthyDeps: '{ok}/{total} dependencies healthy',
    pendingRate: '{rate}% pending',
    completionRate: '{rate}% completed',
    recommendationTotal: '{count} recommendations',
    pendingRecs: 'Pending recommendations',
    avgRecs: 'Avg per task',
    distTitle: 'Recommendations per Task',
    distTotal: '{count} tasks',
    noTasks: 'No task data',
  },

  // Knowledge base overview
  knowledge: {
    title: 'Knowledge Base',
    subtitle: '{count} code systems',
    icd: 'ICD-10 diagnosis index',
    cm3: 'ICD-9-CM3 procedure index',
    tumor: 'Tumor site index',
    synonyms: 'Term synonyms',
    rules: 'Coding rules',
    medicalCodes: 'Medical codes',
    validLead: '{count} valid lead words',
    open: 'Open knowledge base',
    empty: 'No knowledge data',
  },

  // Metric descriptions
  activeUsersRatio: 'Active users ratio {ratio}%',
  monthlyTargetComplete: 'Monthly target completion {ratio}%',
  pendingOrders: 'Pending orders {count}',
  aboveIndustry: 'Above industry average {ratio}%',

  // Monthly performance
  monthlyPerformance: 'Monthly Performance',
  realTimeMonitor: 'Real-time monitoring of business data and key metrics',

  // This week
  thisWeek: 'This Week',
  thisWeekRevenue: 'This Week Revenue',
  thisWeekOrders: 'This Week Orders',
  newCustomers: 'New Customers',

  // Trend
  weeklyTrend: 'Weekly Trend',
  weeklyTrendDesc: 'Data changes in the past 7 days',

  // Quick entry
  quickEntry: 'Quick Entry',
  newOrder: 'New Order',
  addUser: 'Add User',
  generateReport: 'Generate Report',
  systemSettings: 'System Settings',

  // System status
  systemStatus: 'System Status',
  operational: 'Operational',
  degraded: 'Degraded',
  unhealthy: 'Unhealthy',
  statusUnknown: 'Unknown',
  searchEngine: 'Search Engine',
  apiService: 'API Service',
  database: 'Database',
  messageQueue: 'Message Queue',

  // Order status
  orderStatus: 'Order Status',
  newOrders: 'New Orders',
  processing: 'Processing',
  shipping: 'Shipping',
  completed: 'Completed',

  // Todo items
  todoItems: 'Todo Items',
  inventoryWarning: 'Inventory Warning',
  pendingReview: 'Pending Review',
  pendingRefund: 'Pending Refund',
  systemNotification: 'System Notification',

  // Activities
  recentActivities: 'Recent Activities',
  success: 'Success',
  warning: 'Warning',
  error: 'Error',
  info: 'Info',

  // Time
  minutesAgo: '{count} minutes ago',
  hoursAgo: '{count} hours ago',

  // Days
  mon: 'Mon',
  tue: 'Tue',
  wed: 'Wed',
  thu: 'Thu',
  fri: 'Fri',
  sat: 'Sat',
  sun: 'Sun',

  // View details
  viewDetails: 'View Details',

  // System status
  availableRate: 'Available {uptime}',

  // Processing efficiency
  avgProcessingTime: 'Avg Processing Time',
  paymentProcessing: 'Payment Processing',
  orderConfirmation: 'Order Confirmation',
  shippingProcessing: 'Shipping Processing',
  customerResponse: 'Customer Response',
  seconds: '{count}s',
  minutes: '{count} min',
}
