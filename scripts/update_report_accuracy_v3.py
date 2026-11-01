# -*- coding: utf-8 -*-
"""总汇报 docx 脚本准确性修正（v2，逐条独立替换，容忍已修复项）。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

p = r"scripts\build_project_report_docx.py"
c = open(p, encoding="utf-8").read()

fixes = [
    # 1. 端点数
    (
        "约 70 个 REST 端点",
        "75 个 REST 端点",
    ),
    # 2. 迁移数
    (
        "SQL Server（EF Core，21 个迁移）",
        "SQL Server（EF Core，23 个迁移）",
    ),
    # 3. ES 认证
    (
        "BM25 词法检索 + dense_vector 向量检索",
        "BM25 词法检索 + dense_vector 向量检索；生产启用 xpack.security 认证",
    ),
    # 4. LLM 行 0.5b → 3b
    (
        "本地 qwen2.5:0.5b；证据引用维受 0.5B 模型能力限制（诚实降级）",
        "本地 qwen2.5:3b-instruct（GPU）；证据引用维已恢复可得分（10-31 升级）",
    ),
    # 5. 风险表向量行
    (
        "向量补位推荐无法引用索引链路证据（0.5B 模型给不出证据链），2767 条 Top1 中仅 1877 条可沿证据链回溯",
        "旧模型（0.5B）时代向量补位无法引用索引链路证据；3B 升级后 LLM 维恢复完整（向量通道本身不产证据链引用）",
    ),
]

applied = 0
skipped = 0
for old, new in fixes:
    if old in c:
        c = c.replace(old, new, 1)
        applied += 1
    elif new in c:
        skipped += 1
    else:
        print(f"MISS: {old[:50]}")

open(p, "w", encoding="utf-8").write(c)
print(f"applied={applied} skipped(already)={skipped}")
