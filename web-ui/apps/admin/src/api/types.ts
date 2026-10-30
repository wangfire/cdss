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

/** 后端 AppUserSummaryResponse：用户列表行，附带角色代码与登录锁定状态。 */
export interface AppUserSummaryDto {
  id: string
  code: string
  displayName: string
  status: string
  createdAt: string
  lastLoginAt?: string | null
  roles: string[]
  /** 登录锁定状态（防暴力破解）；后端旧版本缺省视为未锁定。 */
  lockout?: UserLockoutDto | null
}

/** 登录锁定状态摘要：locked=false 时锁定时间为空。 */
export interface UserLockoutDto {
  locked: boolean
  failedLoginCount: number
  lockedUntil?: string | null
  lockoutAt?: string | null
}

/** 内置角色模板（正式权限矩阵）：创建角色时可整体套用。 */
export interface RoleTemplateDto {
  code: string
  name: string
  description: string
  permissions: string[]
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
