# -*- coding: utf-8 -*-
"""WP-E：user.ts defaultAvatar 改本地 data URI SVG（去 dicebear 外链）。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

p = r"web-ui\apps\admin\src\stores\user.ts"
c = open(p, encoding="utf-8").read()

old = """  const defaultAvatar = computed(() => {
    if (user.value?.avatar) return user.value.avatar
    const name = user.value?.userName || user.value?.displayName || 'Admin'
    return `https://api.dicebear.com/7.x/avataaars/svg?seed=${encodeURIComponent(name)}&backgroundColor=b6e3f4,c0aede,d1d4f9,ffd5dc,ffdfbf`
  })"""

new = """  const defaultAvatar = computed(() => {
    if (user.value?.avatar) return user.value.avatar
    const name = user.value?.userName || user.value?.displayName || 'Admin'
    return generateLocalAvatar(name)
  })"""

assert old in c, "dicebear anchor"
c = c.replace(old, new, 1)

# 追加本地头像生成函数（文件尾 store 定义外）
helper = """

/**
 * 本地内联 SVG 头像（WP-E 去外链）：按用户名 hash 取色 + 首字母，
 * 无任何外部请求（内网部署 dicebear 不可达会导致头像裂图）。
 */
const AVATAR_PALETTE = [
  'b6e3f4', 'c0aede', 'd1d4f9', 'ffd5dc', 'ffdfbf',
  'c1e4c1', 'f5d0c5', 'd4d4d4', 'e8d5f4', 'f9e2b8',
]

function generateLocalAvatar(name: string): string {
  let hash = 0
  for (let i = 0; i < name.length; i++) {
    hash = (hash * 31 + name.charCodeAt(i)) >>> 0
  }
  const bg = AVATAR_PALETTE[hash % AVATAR_PALETTE.length]
  const initial = name.trim().charAt(0).toUpperCase() || 'U'
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">`
    + `<rect width="64" height="64" fill="#${bg}"/>`
    + `<text x="32" y="42" font-family="system-ui,sans-serif" font-size="28" `
    + `font-weight="600" fill="#333" text-anchor="middle">${initial}</text></svg>`
  return `data:image/svg+xml;base64,${btoa(unescape(encodeURIComponent(svg)))}`
}"""

# 插到 defineStore 之前（文件尾 append 亦可，但函数需在 computed 之前定义——JS 函数提升对 const 不适用，
# 所以放 defaultAvatar 之前最稳：直接在 "const defaultAvatar" 前插入 helper）
helper_anchor = "  const defaultAvatar = computed(() => {"
c = c.replace(helper_anchor, helper + "\n" + helper_anchor, 1)

open(p, "w", encoding="utf-8").write(c)
print("local avatar added")
