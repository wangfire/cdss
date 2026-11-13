import { describe, expect, it } from 'vitest'

/**
 * 权限判定逻辑测试。
 *
 * hasAnyPermission / hasAllPermissions 的行为约定：
 * - 空数组或 undefined → true（调用方未传权限约束 = 不限权）
 * - 非空数组 → 逐一比对用户权限列表
 *
 * 注意：hasAnyPermission([]) 返回 true 是既有语义（"调用方没设门槛"），
 * 如果后续改为"空数组 = 全拒绝"，需要同步排查所有调用点。
 */
describe('hasAnyPermission', () => {
  // 模拟 store 里的判定逻辑（提取为纯函数以便测试，不依赖 Pinia）
  function checkAny(userPermissions: string[], required?: string[]): boolean {
    if (!required || required.length === 0) return true
    return required.some(p => userPermissions.includes(p))
  }

  it('空数组返回 true（不限权）', () => {
    expect(checkAny(['coding.read'], [])).toBe(true)
    expect(checkAny([], [])).toBe(true)
  })

  it('undefined 返回 true（不限权）', () => {
    expect(checkAny(['coding.read'], undefined)).toBe(true)
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
    if (!required || required.length === 0) return true
    return required.every(p => userPermissions.includes(p))
  }

  it('空数组返回 true（不限权）', () => {
    expect(checkAll(['coding.read'], [])).toBe(true)
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
