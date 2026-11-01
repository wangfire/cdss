# -*- coding: utf-8 -*-
"""总汇报 docx 最终修正：迁移数 21→23、最近提交日期、i18n 状态更新、dicebear 上下文确认。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

p = r"scripts\build_project_report_docx.py"
c = open(p, encoding="utf-8").read()

fixes = [
    # 1. 迁移数 21 → 23（6.3 交付物段）
    (
        "数据库迁移：21 个 EF Core 迁移（InitialCreate → 登录锁定 20261030090000 / 权限矩阵种子 20261030091000），迁移链完整、可从空库重建。",
        "数据库迁移：23 个 EF Core 迁移（InitialCreate → 登录锁定 20261030090000 / 权限矩阵种子 20261030091000 / 索引表停用开关 20261031140000），迁移链完整、可从空库重建。",
    ),
    # 2. 最近提交日期
    (
        "代码：分支 codex-phase2-coding-mvp，最近提交 2026-10-28；工作区当前仅余 12 个未跟踪文件（多为评测台账与报告），主体成果已全部入库。",
        "代码：分支 codex-phase2-coding-mvp，最近提交 2026-11-01；本期新增 16 个 commit（RBAC 收口/LLM 3B/CAP 对比/知识库治理/生产部署/前端生产化），主体成果已全部入库。",
    ),
    # 3. 文档数 14 → 17（新增 4 份方案/报告 + 1 份 RBAC 交付说明）
    (
        "文档：14 份阶段方案/汇报/口径规范（docs/，含 2026-10-30 RBAC 收口交付说明）",
        "文档：17 份阶段方案/汇报/口径规范（docs/，含 RBAC 收口交付说明、LLM 3B A/B 报告、CAP 对比报告、生产部署/前端生产化/知识库治理三份方案）",
    ),
    # 4. i18n 状态更新（第五章 5.2 或第八章里的"列入下期"改为已完成）
    (
        "静态布局文案余量低频，列入下期",
        "静态布局文案已于 10-31 收口（复扫 0 行）",
    ),
]

applied = 0
for old, new in fixes:
    if old in c:
        c = c.replace(old, new, 1)
        applied += 1
    else:
        print(f"MISS: {old[:60]}")

open(p, "w", encoding="utf-8").write(c)
print(f"applied: {applied}")
