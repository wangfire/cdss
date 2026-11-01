# -*- coding: utf-8 -*-
"""index.ts 注册 layout 模块 + common.mainNav 兜底。"""
import io
import os
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

zh_dir = r"web-ui\apps\admin\src\i18n\locales\zh-CN"
en_dir = r"web-ui\apps\admin\src\i18n\locales\en-US"

for lang, d in (("zh-CN", zh_dir), ("en-US", en_dir)):
    idx = os.path.join(d, "index.ts")
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

    # common.mainNav 兜底（layout.mainNav 引用不到时用 common）
    common = os.path.join(d, "common.ts")
    cc = open(common, encoding="utf-8").read()
    if "mainNav" not in cc:
        lines = cc.rstrip("\n").split("\n")
        close_idx = next(i for i in range(len(lines) - 1, -1, -1) if lines[i].strip() == "}")
        val = "主导航" if lang == "zh-CN" else "Main navigation"
        lines.insert(close_idx, f"  mainNav: '{val}',")
        open(common, "w", encoding="utf-8").write("\n".join(lines))
        print(common, "common.mainNav added")
