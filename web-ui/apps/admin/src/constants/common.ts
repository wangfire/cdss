/**
 * 本地存储键名常量
 */
export const STORAGE_KEYS = {
  /** 语言设置存储键 */
  LOCALE: 'tabtab-locale',
  /** 主题设置存储键 */
  THEME: 'tabtab-theme',
  /** 用户信息存储键 */
  USER: 'tabtab-user',
  /** 登录 token 键 */
  TOKEN: 'tabtab-token',
  /** 登录医院 ID 键（用于 X-Hospital-Id 头与后续请求的医院隔离上下文） */
  HOSPITAL: 'tabtab-hospital',
} as const

/**
 * API 基础配置
 */
export const API_CONFIG = {
  /** 后端 API 基础地址（开发环境指向本地 ASP.NET Core） */
  BASE_URL: 'http://localhost:5080',
  /** 请求超时时间（毫秒） */
  TIMEOUT: 15_000,
} as const
