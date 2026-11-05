# -*- coding: utf-8 -*-
"""安全测试补登录失败/锁定触发审计断言（v2，处理 CRLF）。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

f = r"tests\HospitalAi.Api.Tests\ApiSecurityCloseoutTests.cs"
data = open(f, "rb").read().decode("utf-8")

if "app_user.lockout_triggered" in data:
    print("already present")
    sys.exit(0)

nl = "\r\n" if "\r\n" in data else "\n"

old = (
    "        // 解锁动作必须落审计日志（操作者 + 被解锁账号），可追溯绕过锁定的运维行为。" + nl
    + "        await using var verifyContext = database.CreateContext();" + nl
    + "        var audit = await verifyContext.AuditLogs"
)
assert old in data, "anchor"

new = (
    "        // 登录失败与锁定触发也应留痕（暴力破解溯源）：4 次普通失败 + 1 次锁定触发。" + nl
    + "        var failAuditCount = await verifyContext.AuditLogs" + nl
    + "            .CountAsync(" + nl
    + "                item => item.ResourceType == \"app_user\"" + nl
    + "                    && item.ResourceId == target.Id" + nl
    + "                    && (item.Action == \"app_user.login_failed\" || item.Action == \"app_user.lockout_triggered\"));" + nl
    + "        Assert.True(failAuditCount >= 5, $\"失败+锁定审计应 ≥5 条，实际 {failAuditCount}\");" + nl
    + "        var lockoutTriggered = await verifyContext.AuditLogs" + nl
    + "            .SingleAsync(" + nl
    + "                item => item.ResourceType == \"app_user\"" + nl
    + "                    && item.ResourceId == target.Id" + nl
    + "                    && item.Action == \"app_user.lockout_triggered\");" + nl
    + "        Assert.Equal(\"LOCKED\", lockoutTriggered.Result);" + nl
    + nl
    + "        // 解锁动作必须落审计日志（操作者 + 被解锁账号），可追溯绕过锁定的运维行为。" + nl
    + "        var audit = await verifyContext.AuditLogs"
)
data = data.replace(old, new, 1)
open(f, "wb").write(data.encode("utf-8"))
print("assertions added")
