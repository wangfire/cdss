import { http } from './http'
import type {
  WorkbenchTaskResponse,
  WorkbenchTasksPageResponse,
  WorkbenchTaskStatsResponse,
  CodingTaskResponse,
  CodingTaskRecommendationsResponse,
  ReviewCodingTaskRequest,
  CodingReviewResponse,
  CodingTaskStatus,
  PipelineRunResponse,
  PipelineConfigResponse,
  PipelineRerunResponse,
} from './types-cdss'

export const workbenchApi = {
  /** 工作台任务服务端分页（WP-B 前端生产化）：keyword 数据库端过滤姓名/病案号。 */
  listTasksPage: (
    params?: { status?: CodingTaskStatus; keyword?: string; page?: number; pageSize?: number },
  ) => http.get<WorkbenchTasksPageResponse>('/api/v1/workbench/tasks', params),

  /** 任务状态分组计数（仪表盘统计卡，数据库端 GROUP BY）。 */
  getTaskStats: () => http.get<WorkbenchTaskStatsResponse>('/api/v1/workbench/tasks/stats'),

  getTask: (taskId: string) =>
    http.get<CodingTaskResponse>(`/api/v1/coding-tasks/${taskId}`),

  getRecommendations: (taskId: string) =>
    http.get<CodingTaskRecommendationsResponse>(
      `/api/v1/coding-tasks/${taskId}/recommendations`,
    ),

  getTraces: (taskId: string) =>
    http.get<PipelineRunResponse[]>(
      `/api/v1/coding-tasks/${taskId}/traces`,
    ),

  submitReview: (taskId: string, data: ReviewCodingTaskRequest) =>
    http.post<CodingReviewResponse>(`/api/v1/coding-tasks/${taskId}/review`, data),

  /** 流水线运行时配置（工作台设置抽屉），读/写均需 user.manage 权限。 */
  getPipelineConfig: () => http.get<PipelineConfigResponse>('/api/v1/admin/pipeline-config'),

  updatePipelineConfig: (data: {
    icdTitleDirect: boolean
    cm3TitleDirect: boolean
    icdTitleReverse: boolean
  }) => http.put<PipelineConfigResponse>('/api/v1/admin/pipeline-config', data),

  /** 批量重跑：当前医院全部终态任务置回 Pending 重新入队，返回重跑任务数。 */
  rerunPipeline: () => http.post<PipelineRerunResponse>('/api/v1/admin/pipeline/rerun'),
}
