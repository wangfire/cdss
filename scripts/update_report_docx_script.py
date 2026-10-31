# -*- coding: utf-8 -*-
"""更新总汇报 docx 生成脚本：追加第八章（2026-10-30~31 收口成果）并同步过时数字。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

p = r"scripts\build_project_report_docx.py"
c = open(p, encoding="utf-8").read()

# 1. 目录加第八章
old_toc = '    "七、风险与建议",\n]'
new_toc = '    "七、风险与建议",\n    "八、本期收口成果（2026-10-30 ~ 10-31）",\n]'
assert old_toc in c, "toc anchor"
c = c.replace(old_toc, new_toc, 1)

# 2. 技术栈 LLM 模型行更新（0.5B CPU → 3B GPU）
old_model = "Ollama qwen2.5:0.5b-instruct（LLM 一致性校验），均为 ONNX/CPU 小模型先行"
new_model = "Ollama qwen2.5:3b-instruct（LLM 一致性校验，GPU 推理），向量/重排为 ONNX/CPU 小模型"
assert old_model in c, "model anchor"
c = c.replace(old_model, new_model, 1)

# 3. 迁移数 19 → 21
c = c.replace("SQL Server（EF Core，19 个迁移）", "SQL Server（EF Core，21 个迁移）", 1)

open(p, "w", encoding="utf-8").write(c)
print("toc/model/migrations updated")
