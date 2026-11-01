# -*- coding: utf-8 -*-
"""安全测试补登录失败/锁定触发审计断言（v3，单脚本精准插入）。"""
import io
import re
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

f = r"tests\HospitalAi.Api.Tests\ApiSecurityCloseoutTests.cs"
data = open(f, "rb").read().decode("utf-8")
nl = "\r\n" if "\r\n" in data else "\n"

if "app_user.lockout_triggered" in data:
    print("already present")
    sys.exit(0)

# 插入点：unlockResponse 声明行之前
i_unlock = data.find("        using var unlockResponse = await adminClient.PostAsync(")
assert i_unlock > 0, "unlock line not found"
line_start = data.rfind(nl, 0, i_unlock) + len(nl)

assert_block = (
    "        // 登录失败与锁定触发也应留痕（暴力破解溯源）：4 次普通失败 + 1 次锁定触发。" + nl
    + "        await using var verifyContext = database.CreateContext();" + nl
    + "        var failAuditCount = await verifyContext.AuditLogs" + nl
    + "            .CountAsync(" + nl
    + "                item => item.ResourceType == \"app_user\"" + nl
    + "                    && item.ResourceId == target.Id" + nl
    + "                    && (item.Action == \"app_user.login_failed\" || item.Action == \"app_user.lockout_triggered\"));" + nl
    + "        Assert.True(failAuditCount >= 5, $\"失败+锁定审计应 ≥5 条，实际 {failAuditCount}\");" + nl
    + "        var lockoutAudit = await verifyContext.AuditLogs" + nl
    + "            .SingleAsync(" + nl
    + "                item => item.ResourceType == \"app_user\"" + nl
    + "                    && item.ResourceId == target.Id" + nl
    + "                    && item.Action == \"app_user.lockout_triggered\");" + nl
    + "        Assert.Equal(\"LOCKED\", lockoutAudit.Result);" + nl
    + "        Assert.Equal(\"FAILED\", (await verifyContext.AuditLogs" + nl
    + "            .SingleAsync(item => item.Action == \"app_user.login_failed\")).Result);" + nl
    + nl
)

data = data[:line_start] + assert_block + data[line_start:]

# 把后续旧审计段的 verifyContext 声明去掉（已提前声明）
old_decl = (
    "        // 解锁动作必须落审计日志（操作者 + 被解锁账号），可追溯绕过锁定的运维行为。" + nl
    + "        await using var verifyContext = database.CreateContext();" + nl
)
new_decl = (
    "        // 解锁动作必须落审计日志（操作者 + 被解锁账号），可追溯绕过锁定的运维行为。" + nl
)
assert old_decl in data, "old decl after insert"
data = data.replace(old_decl, new_decl, 1)

open(f, "wb").write(data.encode("utf-8"))
print("assertions inserted before unlock")
