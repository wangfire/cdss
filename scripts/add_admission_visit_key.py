# -*- coding: utf-8 -*-
"""i18n：caseEntry.ts 补 admissionVisit key（zh/en）。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

files = [
    (
        r"web-ui\apps\admin\src\i18n\locales\zh-CN\caseEntry.ts",
        {"admissionVisit": "第 {count} 次住院"},
    ),
    (
        r"web-ui\apps\admin\src\i18n\locales\en-US\caseEntry.ts",
        {"admissionVisit": "Admission #{count}"},
    ),
]

for path, keys in files:
    c = open(path, encoding="utf-8").read()
    if "admissionVisit" in c:
        print(path, "already")
        continue
    lines = c.rstrip("\n").split("\n")
    close_idx = next(i for i in range(len(lines) - 1, -1, -1) if lines[i].strip() == "}")
    additions = "\n".join(f"  {k}: '{v}'," for k, v in keys.items())
    lines.insert(close_idx, additions)
    open(path, "w", encoding="utf-8").write("\n".join(lines))
    print(path, "keys added")
