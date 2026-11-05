# -*- coding: utf-8 -*-
"""WP1 API 端点：GET/PUT /admin/pipeline-config + POST /admin/pipeline/rerun（批量重跑）。

端点设计（全部挂管理员权限）：
  GET  /api/v1/admin/pipeline-config   → 当前生效配置（appsettings 基线 + DB 覆盖合并视图）
  PUT  /api/v1/admin/pipeline-config   → 更新覆盖项（body: { icdTitleDirect, cm3TitleDirect, icdTitleReverse }）
  POST /api/v1/admin/pipeline/rerun    → 批量重跑：按医院把全部终态任务重新入队（Outbox 派发）

重跑实现：直接往 coding_task 写回 status=PENDING + 复用现有 outbox 派发逻辑。
先查现有派发（outbox 表名与消息写入方式）。
"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

# 查 outbox 与任务状态字段
import glob

ctx = glob.glob(r"src\HospitalAi.Infrastructure\SqlServer\HospitalAiDbContext.cs")[0]
c = open(ctx, encoding="utf-8").read()
for name in ("OutboxMessage", "IntegrationEvent"):
    if name in c:
        print("has:", name)
