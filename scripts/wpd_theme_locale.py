# -*- coding: utf-8 -*-
"""i18n：theme keys（zh/en） + DashboardLayout aria-label + CaseEntry 第X次住院。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

# 1. theme locale 文件（新建或追加到 settings）
import os

zh_dir = r"web-ui\apps\admin\src\i18n\locales\zh-CN"
en_dir = r"web-ui\apps\admin\src\i18n\locales\en-US"

# 检查是否已有 theme.ts
theme_zh = os.path.join(zh_dir, "theme.ts")
theme_en = os.path.join(en_dir, "theme.ts")

zh_content = """/**
 * 主题切换 i18n
 */
export default {
  light: '浅色',
  dark: '深色',
  system: '系统',
  modeSuffix: '模式',
  switchAriaLabel: '切换主题，当前: {mode}',
}
"""
en_content = """/**
 * Theme i18n
 */
export default {
  light: 'Light',
  dark: 'Dark',
  system: 'System',
  modeSuffix: ' mode',
  switchAriaLabel: 'Switch theme, current: {mode}',
}
"""

for path, content in ((theme_zh, zh_content), (theme_en, en_content)):
    if not os.path.exists(path):
        open(path, "w", encoding="utf-8").write(content)
        print("created:", path)
    else:
        c = open(path, encoding="utf-8").read()
        if "switchAriaLabel" not in c:
            # 追加到闭合 } 前
            lines = c.rstrip("\n").split("\n")
            close_idx = next(i for i in range(len(lines) - 1, -1, -1) if lines[i].strip() == "}")
            keys = "\n".join(f"  {k}: '{v}'," for k, v in content_to_keys(content).items())
            lines.insert(close_idx, keys)
            open(path, "w", encoding="utf-8").write("\n".join(lines))
            print("appended:", path)
        else:
            print("already has switchAriaLabel:", path)

def content_to_keys(content):
    import re
    keys = {}
    for m in re.finditer(r"(\w+):\s*'([^']+)'", content):
        keys[m.group(1)] = m.group(2)
    return keys
