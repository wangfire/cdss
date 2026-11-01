# -*- coding: utf-8 -*-
"""DashboardLayout aria-label i18n + layout locale 文件。"""
import io
import os
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

# 1. DashboardLayout.vue aria-label 替换
p = r"web-ui\apps\admin\src\layouts\DashboardLayout.vue"
c = open(p, encoding="utf-8").read()
old_aria = 'aria-label="主导航"'
new_aria = ':aria-label="t(\'layout.mainNav\')"'
count = c.count(old_aria)
c = c.replace(old_aria, new_aria)
open(p, "w", encoding="utf-8").write(c)
print(f"DashboardLayout aria replaced x{count}")

# 2. layout locale 文件
zh_dir = r"web-ui\apps\admin\src\i18n\locales\zh-CN"
en_dir = r"web-ui\apps\admin\src\i18n\locales\en-US"
zh_layout = os.path.join(zh_dir, "layout.ts")
en_layout = os.path.join(en_dir, "layout.ts")

zh_content = """/**
 * 布局 i18n
 */
export default {
  mainNav: '主导航',
}
"""
en_content = """/**
 * Layout i18n
 */
export default {
  mainNav: 'Main navigation',
}
"""

for path, content in ((zh_layout, zh_content), (en_layout, en_content)):
    if not os.path.exists(path):
        open(path, "w", encoding="utf-8").write(content)
        print("created:", path)

# 3. index.ts 注册 layout
for lang in ("zh-CN", "en-US"):
    idx = os.path.join(lang_dir if lang == "zh-CN" else en_dir, "index.ts")
    ic = open(idx, encoding="utf-8").read()
    if "import layout" not in ic:
        ic = ic.replace(
            "import theme from './theme'",
            "import theme from './theme'\nimport layout from './layout'",
            1,
        )
        ic = ic.replace("  theme,", "  theme,\n  layout,", 1)
        open(idx, "w", encoding="utf-8").write(ic)
        print(idx, "layout registered")

# 4. layout keys 也追加到 common.ts（更保险，避免 i18n 注册遗漏）
for lang, val in (("zh-CN", "主导航"), ("en-US", "Main navigation")):
    idx = os.path.join(
        zh_dir if lang == "zh-CN" else en_dir, "common.ts"
    )
    cc = open(idx, encoding="utf-8").read()
    if "mainNav" not in cc:
        lines = cc.rstrip("\n").split("\n")
        close_idx = next(i for i in range(len(lines) - 1, -1, -1) if lines[i].strip() == "}")
        lines.insert(close_idx, f"  mainNav: '{val}',")
        open(idx, "w", encoding="utf-8").write("\n".join(lines))
        print(idx, "common.mainNav added")
