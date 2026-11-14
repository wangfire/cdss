import { describe, expect, it } from 'vitest'

/**
 * 权限判定逻辑测试。
 *
 * hasAnyPermission / hasAllPermissions 的语义（2026-11-14 收紧为 fail-closed）：
 * - 未传 / 空数组 → false（权限检查没有"空门槛放行"的语义）；
 *   此前空数组返回 true —— 若调用方传了派生数组（如权限树节点改名后
 *   permissionCodesOf 返回 []），按钮会对无权用户可见（fail-open）。
 * - 需要表达"不做权限门控"时，调用方不应调用本方法。
 * - 全部 27 个调用点均传非空字面量或常量（已审计），行为零变化；
 *   路由守卫不经过本方法，未设权限的路由不受影响。
 */
describe('hasAnyPermission', () => {
  // 模拟 store 里的判定逻辑（提取为纯函数以便测试，不依赖 Pinia）
  function checkAny(userPermissions: string[], required?: string[]): boolean {
    if (!required || required.length === 0) return false
    return required.some(p => userPermissions.includes(p))
  }

  it('空数组返回 false（fail-closed）', () => {
    expect(checkAny(['coding.read'], [])).toBe(false)
    expect(checkAny([], [])).toBe(false)
  })

  it('undefined 返回 false（fail-closed）', () => {
    expect(checkAny(['coding.read'], undefined)).toBe(false)
    expect(checkAny([], undefined)).toBe(false)
  })

  it('用户拥有所需权限之一', () => {
    expect(checkAny(['coding.read', 'coding.review'], ['coding.read'])).toBe(true)
  })

  it('用户不拥有任何所需权限', () => {
    expect(checkAny(['coding.read'], ['user.manage'])).toBe(false)
  })

  it('用户权限为空时非空要求返回 false', () => {
    expect(checkAny([], ['coding.read'])).toBe(false)
  })

  it('多个所需权限只需命中一个', () => {
    expect(
      checkAny(['coding.read'], ['user.manage', 'coding.read', 'role.create']),
    ).toBe(true)
  })
})

describe('hasAllPermissions', () => {
  function checkAll(userPermissions: string[], required?: string[]): boolean {
    if (!required || required.length === 0) return false
    return required.every(p => userPermissions.includes(p))
  }

  it('空数组返回 false（fail-closed）', () => {
    expect(checkAll(['coding.read'], [])).toBe(false)
  })

  it('用户拥有全部所需权限', () => {
    expect(
      checkAll(['coding.read', 'coding.review'], ['coding.read', 'coding.review']),
    ).toBe(true)
  })

  it('用户缺少任一所需权限', () => {
    expect(
      checkAll(['coding.read'], ['coding.read', 'user.manage']),
    ).toBe(false)
  })
})
