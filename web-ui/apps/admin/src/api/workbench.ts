import { http } from './http'
import type {
  WorkbenchTaskResponse,
  WorkbenchTasksPageResponse,
  CodingTaskResponse,
  CodingTaskRecommendationsResponse,
  ReviewCodingTaskRequest,
  CodingReviewResponse,
  CodingTaskStatus,
  PipelineRunResponse,
} from './types-cdss'

export const workbenchApi = {
  /** 工作台任务服务端分页（WP-B 前端生产化）：keyword 数据库端过滤姓名/病案号。 */
  listTasksPage: (
    params?: { status?: CodingTaskStatus; keyword?: string; page?: number; pageSize?: number },
  ) => http.get<WorkbenchTasksPageResponse>('/api/v1/workbench/tasks', params),

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
}
