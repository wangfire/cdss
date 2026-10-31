# -*- coding: utf-8 -*-
"""WP-D：Workbench 搜索框 placeholder i18n。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

p = r"web-ui\apps\admin\src\views\Workbench.vue"
c = open(p, encoding="utf-8").read()
old = 'placeholder="按姓名、病案号查询..."'
new = ':placeholder="t(\'workbench.searchPlaceholder\')"'
if old in c:
    c = c.replace(old, new, 1)
    open(p, "w", encoding="utf-8").write(c)
    print("placeholder i18n done")
else:
    print("placeholder not found (may already be i18n)")
