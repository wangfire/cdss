# -*- coding: utf-8 -*-
"""i18n：workbench.ts 补 searchPlaceholder key（zh/en）。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

files = [
    (
        r"web-ui\apps\admin\src\i18n\locales\zh-CN\workbench.ts",
        {"searchPlaceholder": "按姓名、病案号查询..."},
    ),
    (
        r"web-ui\apps\admin\src\i18n\locales\en-US\workbench.ts",
        {"searchPlaceholder": "Search by patient name or record no..."},
    ),
]

for path, keys in files:
    c = open(path, encoding="utf-8").read()
    if "searchPlaceholder" in c:
        print(path, "already present")
        continue
    lines = c.rstrip("\n").split("\n")
    close_idx = next(i for i in range(len(lines) - 1, -1, -1) if lines[i].strip() == "}")
    additions = "\n".join(f"  {k}: '{v}'," for k, v in keys.items())
    lines.insert(close_idx, additions)
    open(path, "w", encoding="utf-8").write("\n".join(lines))
    print(path, "keys added")
