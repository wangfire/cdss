# -*- coding: utf-8 -*-
"""修复 runner 日志行（message 不在作用域）。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

p = r"src\HospitalAi.Worker\Pipeline\V22LiteCodingPipelineRunner.cs"
c = open(p, encoding="utf-8").read()
old = '_logger.LogWarning(ex, "流水线运行时配置读取失败，使用 appsettings 基线。TaskId={TaskId}", message.TaskId);'
new = '_logger.LogWarning(ex, "流水线运行时配置读取失败，使用 appsettings 基线。");'
if old in c:
    c = c.replace(old, new, 1)
    open(p, "w", encoding="utf-8").write(c)
    print("log line fixed")
else:
    print("already fixed or not found")
