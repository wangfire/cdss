# -*- coding: utf-8 -*-
"""WP1：流水线开关动态化——运行时配置表 + API 读取。

设计：
  - 新表 pipeline_runtime_config（单行 key/value JSON，per-hospital 可扩展，先做全局单行）：
      id, config_key('v22lite'), config_json, updated_at
  - Worker：PipelineRuntimeConfigSource（单例）——启动加载 DB 配置，
    每次 runner 执行时 GetEffective() 把 DB 覆盖项合并到 IOptions 默认值上；
    API PUT /admin/pipeline-config 后写 DB 并通知（轮询间隔 5s 或直接每任务读 DB）。
  简化实现：runner 每次 StartAsync 时从 DB 读一次配置（任务级延迟低，单行查询）。
"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

# 1. Domain/Infrastructure：配置实体与 EF 映射
# 先看现有 DbContext 与实体目录结构
import os

print("DbContext:", os.path.exists(r"src\HospitalAi.Infrastructure\SqlServer\HospitalAiDbContext.cs"))
print("Entities dir:", os.path.isdir(r"src\HospitalAi.Infrastructure\SqlServer\Entities"))
