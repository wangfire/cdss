# -*- coding: utf-8 -*-
"""总汇报 docx：封面汇报日期/分支提交日期更新。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

p = r"scripts\build_project_report_docx.py"
c = open(p, encoding="utf-8").read()

fixes = [
    (
        '("汇报日期", "2026 年 10 月 30 日")',
        '("汇报日期", "2026 年 11 月 1 日")',
    ),
    (
        '("代码分支", "codex-phase2-coding-mvp（最近提交 2026-10-28）")',
        '("代码分支", "codex-phase2-coding-mvp（最近提交 2026-11-01）")',
    ),
]
for old, new in fixes:
    assert old in c, old[:50]
    c = c.replace(old, new, 1)

open(p, "w", encoding="utf-8").write(c)
print("cover dates updated")
