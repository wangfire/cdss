/**
 * 通用 API 类型定义
 */

export interface UserDto {
  id: string
  userName: string
  email: string
  displayName?: string | null
  avatar?: string | null
  phone?: string | null
  isActive: boolean
  createdAt: string
  lastLoginAt?: string | null
  roles: string[]
}

export interface LoginRequest {
  userName: string
  password: string
  /** 医院 GUID：后端按医院隔离，登录必须携带。 */
  hospitalId: string
}

export interface LoginResponse {
  token: string
  expiresAt: string
  user: UserDto
  roles: string[]
  permissions: string[]
}

export interface CurrentUserDto {
  user: UserDto
  roles: string[]
  permissions: string[]
}

export interface ChangePasswordRequest {
  oldPassword: string
  newPassword: string
}

/* ============== 用户与角色（RBAC） ============== */

/** 后端 AppUserResponse：新建/幂等返回的用户详情。 */
export interface AppUserDto {
  id: string
  hospitalId: string
  code: string
  displayName: string
  /** ACTIVE / DISABLED */
  status: string
  createdAt: string
}

/** 后端 AppUserSummaryResponse：用户列表行，附带角色代码。 */
export interface AppUserSummaryDto {
  id: string
  code: string
  displayName: string
  status: string
  createdAt: string
  lastLoginAt?: string | null
  roles: string[]
}

export interface AppRoleDto {
  id: string
  hospitalId: string
  code: string
  name: string
  description: string
  createdAt: string
  /** ADMIN 角色返回全部权限码。 */
  permissions: string[]
}

export interface CreateAppUserRequest {
  code: string
  displayName: string
}

export interface CreateAppRoleRequest {
  code: string
  name: string
  description?: string | null
}
