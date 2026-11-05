# -*- coding: utf-8 -*-
"""修正重跑端点：OutboxStore 是具体类（非接口），签名 Create(messageId, hospitalId, ...)。
注入 OutboxStore，Create 三参调用，Add(message)。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

p = r"src\HospitalAi.Api\Program.cs"
c = open(p, encoding="utf-8-sig").read()

# 1. 注入改 OutboxStore（具体类）
c = c.replace(
    """    async (
        HospitalAiDbContext db,
        IOutboxStore outbox,
        IRequestContext requestContext,
        CancellationToken cancellationToken) =>""",
    """    async (
        HospitalAiDbContext db,
        OutboxStore outbox,
        IRequestContext requestContext,
        CancellationToken cancellationToken) =>""",
    1,
)

# 2. Create 调用签名（messageId, hospitalId, type, payload）
c = c.replace(
    """            outbox.Add(OutboxMessage.Create(
                message.MessageId,
                task.HospitalId,
                "coding.task.created",
                System.Text.Json.JsonSerializer.Serialize(message, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))));""",
    """            outbox.Add(OutboxMessage.Create(
                message.MessageId,
                task.HospitalId,
                "coding.task.created",
                System.Text.Json.JsonSerializer.Serialize(message, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))));""",
    1,
)

open(p, "w", encoding="utf-8-sig").write(c)
print("outbox call fixed")
