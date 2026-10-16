import { http } from './http'
import type {
  AppRoleDto,
  AppUserDto,
  AppUserSummaryDto,
  CreateAppRoleRequest,
  CreateAppUserRequest,
} from './types'

/**
 * 用户与角色管理：全部端点受 user-admin 策略保护（要求 ADMIN 角色），
 * 医院隔离上下文由 http 客户端注入的 X-Hospital-Id 提供。
 */
export const securityApi = {
  listUsers: () => http.get<AppUserSummaryDto[]>('/api/v1/users'),
  createUser: (data: CreateAppUserRequest) => http.post<AppUserDto>('/api/v1/users', data),
  setUserStatus: (userId: string, status: 'ACTIVE' | 'DISABLED') =>
    http.patch<{ ok: boolean }>(`/api/v1/users/${userId}/status`, { status }),

  listRoles: () => http.get<AppRoleDto[]>('/api/v1/roles'),
  createRole: (data: CreateAppRoleRequest) => http.post<AppRoleDto>('/api/v1/roles', data),
  assignRole: (userId: string, roleId: string) =>
    http.post<{ ok: boolean }>(`/api/v1/users/${userId}/roles/${roleId}`),
  removeRole: (userId: string, roleId: string) =>
    http.delete<{ ok: boolean }>(`/api/v1/users/${userId}/roles/${roleId}`),
}
