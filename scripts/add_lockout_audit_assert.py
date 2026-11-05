# -*- coding: utf-8 -*-
"""安全测试补登录失败/锁定触发审计断言。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

f = r"tests\HospitalAi.Api.Tests\ApiSecurityCloseoutTests.cs"
c = open(f, encoding="utf-8").read()

if "app_user.lockout_triggered" in c:
    print("already present")
    sys.exit(0)

anchor = """        // 解锁动作必须落审计日志（操作者 + 被解锁账号），可追溯绕过锁定的运维行为。
        await using var verifyContext = database.CreateContext();"""
assert anchor in c, "audit anchor"

addition = """        // 登录失败与锁定触发也应留痕（暴力破解溯源）：4 次普通失败 + 1 次锁定触发。
        await using var verifyContext = database.CreateContext();
        var failAuditCount = await verifyContext.AuditLogs
            .Where(item => item.ResourceType == "app_user"
                && item.ResourceId == target.Id
                && (item.Action == "app_user.login_failed" || item.Action == "app_user.lockout_triggered"))
            .CountAsync();
        Assert.True(failAuditCount >= 5, $"失败+锁定审计应 ≥5 条，实际 {failAuditCount}");
        var lockoutTriggered = await verifyContext.AuditLogs
            .SingleAsync(item => item.ResourceType == "app_user"
                && item.ResourceId == target.Id
                && item.Action == "app_user.lockout_triggered");
        Assert.Equal("LOCKED", lockoutTriggered.Result);

        // 解锁动作必须落审计日志（操作者 + 被解锁账号），可追溯绕过锁定的运维行为。"""
c = c.replace(anchor, addition, 1)

# 删除紧随其后的原 verifyContext 声明（避免重复）
dup = """        // 解锁动作必须落审计日志（操作者 + 被解锁账号），可追溯绕过锁定的运维行为。
        await using var verifyContext = database.CreateContext();
        var audit = await verifyContext.AuditLogs"""
fixed = """        var audit = await verifyContext.AuditLogs"""
assert dup in c, "dup block"
c = c.replace(dup, fixed, 1)

open(f, "w", encoding="utf-8").write(c)
print("audit assertions added")
