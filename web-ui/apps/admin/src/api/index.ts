import { http } from './http'
import type { LoginRequest, LoginResponse, CurrentUserDto, ChangePasswordRequest } from './types'

/* ============== Auth ============== */
export const authApi = {
  login: (data: LoginRequest) =>
    http.post<LoginResponse>('/api/v1/auth/login', data, { auth: false }),
  me: () => http.get<CurrentUserDto>('/api/v1/auth/me'),
  logout: () => http.post<null>('/api/v1/auth/logout'),
  changePassword: (data: ChangePasswordRequest) =>
    http.post<null>('/api/v1/auth/change-password', data),
}

export * from './http'
export * from './types'
export * from './types-cdss'
export * from './workbench'
export * from './case-entry'
export * from './knowledge'
export * from './health'
