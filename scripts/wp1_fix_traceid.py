# -*- coding: utf-8 -*-
"""重跑端点复用任务原 TraceId（Consumer 按原 TraceId 定位 PipelineTrace）。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

p = r"src\HospitalAi.Api\Program.cs"
c = open(p, encoding="utf-8-sig").read()

old = """            var message = new CodingTaskCreatedMessage(
                Guid.NewGuid(),
                task.HospitalId,
                task.Id,
                task.VisitId,
                task.PipelineVersion,
                Guid.NewGuid().ToString("N"),
                requestContext.IdempotencyKey);"""

new = """            // 复用任务原 TraceId：Consumer 按 hospital_id + task_id + trace_id 定位既有
            // PipelineTrace，新 GUID 会导致"编码任务缺少 Pipeline Trace"异常而空转。
            var originalTraceId = await db.PipelineTraces
                .Where(item => item.CodingTaskId == task.Id && item.HospitalId == task.HospitalId)
                .OrderBy(item => item.CreatedAt)
                .Select(item => item.TraceId)
                .FirstOrDefaultAsync(cancellationToken) ?? Guid.NewGuid().ToString("N");
            var message = new CodingTaskCreatedMessage(
                Guid.NewGuid(),
                task.HospitalId,
                task.Id,
                task.VisitId,
                task.PipelineVersion,
                originalTraceId,
                requestContext.IdempotencyKey);"""

assert old in c, "rerun message anchor"
c = c.replace(old, new, 1)
open(p, "w", encoding="utf-8-sig").write(c)
print("trace id reuse fixed")
