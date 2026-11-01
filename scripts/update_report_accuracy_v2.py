# -*- coding: utf-8 -*-
"""修正 AI 模型行（v2）。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

p = r"scripts\build_project_report_docx.py"
lines = open(p, encoding="utf-8").read().splitlines()

for i, line in enumerate(lines):
    if '"AI 模型", "本地 CPU 推理"' in line:
        lines[i] = line.replace(
            '"AI 模型", "本地 CPU 推理"',
            '"AI 模型", "本地推理（GPU + ONNX/CPU 混合）"',
        ).replace(
            "向量/重排为 ONNX/CPU 小模型",
            "向量/重排为 ONNX/CPU 小模型",
        )
        print(f"line {i+1} fixed")
        break

open(p, "w", encoding="utf-8").write("\n".join(lines))
