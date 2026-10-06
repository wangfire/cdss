import { http } from './http'
import type {
  WorkbenchTaskResponse,
  CodingTaskResponse,
  CodingTaskRecommendationsResponse,
  ReviewCodingTaskRequest,
  CodingReviewResponse,
  CodingTaskStatus,
  PipelineRunResponse,
} from './types-cdss'

export const workbenchApi = {
  listTasks: (status?: CodingTaskStatus) =>
    http.get<WorkbenchTaskResponse[]>('/api/v1/workbench/tasks', status ? { status } : undefined),

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
