import { http } from './http'
import type {
  AppRoleDto,
  AppUserDto,
  AppUserSummaryDto,
  CreateAppRoleRequest,
  CreateAppUserRequest,
  RoleTemplateDto,
} from './types'

/**
 * 用户与角色管理：查看端点要求 user.manage，写端点分别要求
 * user.create / user.status / user.role.assign / role.create / role.permission；
 * 医院隔离上下文由 http 客户端注入的 X-Hospital-Id 提供。
 */
export const securityApi = {
  listUsers: () => http.get<AppUserSummaryDto[]>('/api/v1/users'),
  createUser: (data: CreateAppUserRequest) => http.post<AppUserDto>('/api/v1/users', data),
  setUserStatus: (userId: string, status: 'ACTIVE' | 'DISABLED') =>
    http.patch<{ ok: boolean }>(`/api/v1/users/${userId}/status`, { status }),
  /** 解除登录锁定（清零失败计数并解锁），需 user.status 权限。 */
  unlockUser: (userId: string) =>
    http.post<{ ok: boolean }>(`/api/v1/users/${userId}/unlock`),

  listRoles: () => http.get<AppRoleDto[]>('/api/v1/roles'),
  createRole: (data: CreateAppRoleRequest) => http.post<AppRoleDto>('/api/v1/roles', data),
  updateRolePermissions: (roleId: string, permissions: string[]) =>
    http.put<{ ok: boolean }>(`/api/v1/roles/${roleId}/permissions`, { permissions }),
  assignRole: (userId: string, roleId: string) =>
    http.post<{ ok: boolean }>(`/api/v1/users/${userId}/roles/${roleId}`),
  removeRole: (userId: string, roleId: string) =>
    http.delete<{ ok: boolean }>(`/api/v1/users/${userId}/roles/${roleId}`),

  /** 内置角色模板（正式权限矩阵），创建角色时一键套用。 */
  listRoleTemplates: () =>
    http.get<RoleTemplateDto[]>('/api/v1/security/role-templates'),
}
