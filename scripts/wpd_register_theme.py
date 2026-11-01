# -*- coding: utf-8 -*-
"""i18n index.ts 注册 theme 模块（zh/en）。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

for lang in ("zh-CN", "en-US"):
    p = rf"web-ui\apps\admin\src\i18n\locales\{lang}\index.ts"
    c = open(p, encoding="utf-8").read()
    if "import theme" in c:
        print(p, "already")
        continue
    c = c.replace(
        "import account from './account'",
        "import account from './account'\nimport theme from './theme'",
        1,
    )
    c = c.replace("  account,", "  account,\n  theme,", 1)
    open(p, "w", encoding="utf-8").write(c)
    print(p, "theme registered")
