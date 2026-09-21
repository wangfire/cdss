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
