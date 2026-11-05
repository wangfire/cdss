# -*- coding: utf-8 -*-
"""总汇报 docx 脚本全量准确性修正（基于源码复核）。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

p = r"scripts\build_project_report_docx.py"
c = open(p, encoding="utf-8").read()

fixes = [
    # 1. 端点数 70 → 75（新增匿名医院列表/PATCH enabled/unlock/role-templates 等）
    (
        "约 70 个 REST 端点：编码任务、推荐查询、人工审核、知识库、认证、权限、工作台",
        "75 个 REST 端点：编码任务、推荐查询、人工审核、知识库、认证、权限、工作台、医院公开列表、索引行启停",
    ),
    # 2. 迁移数 21 → 23（LoginLockout/SeedRoleMatrix/EnabledFlag 后实际 23）
    (
        "SQL Server（EF Core，21 个迁移）",
        "SQL Server（EF Core，23 个迁移）",
    ),
    # 3. 检索引擎行补生产认证
    (
        "约 5.3 万条 ICD-10 / ICD-9 编码知识库，BM25 词法检索 + dense_vector 向量检索",
        "约 5.3 万条 ICD-10 / ICD-9 编码知识库，BM25 词法检索 + dense_vector 向量检索；生产启用 xpack.security 认证",
    ),
    # 4. AI 模型行标题 CPU → GPU/ONNX 混合
    (
        '"AI 模型", "本地 CPU 推理", "bge-small-zh-v1.5（语义向量）、bge-reranker-base（重排）、Ollama qwen2.5:3b-instruct（LLM\n一致性校验，GPU 推理），向量/重排为 ONNX/CPU 小模型"',
        '"AI 模型", "本地推理（GPU + ONNX/CPU 混合）", "bge-small-zh-v1.5（语义向量，ONNX）、bge-reranker-base（重排，ONNX）、Ollama qwen2.5:3b-instruct（LLM 一致性校验，GPU 推理）"',
    ),
    # 5. 2.2 表 LLM 行 0.5b → 3b（关键修正）
    (
        "本地 qwen2.5:0.5b；证据引用维受 0.5B 模型能力限制（诚实降级）",
        "本地 qwen2.5:3b-instruct（GPU）；证据引用维已恢复可得分（10-31 升级）",
    ),
    # 6. 风险表向量行：0.5B → 旧模型（避免误解为当前状态）
    (
        "向量补位推荐无法引用索引链路证据（0.5B 模型给不出证据链），2767 条 Top1 中仅 1877 条可沿证据链回溯",
        "旧模型（0.5B）时代向量补位推荐无法引用索引链路证据；3B 升级后 LLM 维恢复完整（向量通道本身不产证据链引用，由索引通道负责）",
    ),
]

for old, new in fixes:
    assert old in c, f"anchor: {old[:60]}"
    c = c.replace(old, new, 1)

open(p, "w", encoding="utf-8").write(c)
print(f"applied {len(fixes)} accuracy fixes")
