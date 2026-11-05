# -*- coding: utf-8 -*-
"""修复插入位置：把新审计断言移到 unlockResponse 之前，并确保 verifyContext 在其前声明。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

f = r"tests\HospitalAi.Api.Tests\ApiSecurityCloseoutTests.cs"
data = open(f, "rb").read().decode("utf-8")
nl = "\r\n" if "\r\n" in data else "\n"

# 当前错误结构：unlockResponse 之后插了断言但 verifyContext 声明被放到了 unlock 之后
# 目标结构：
#   ... unlockResponse.EnsureSuccessStatusCode();
#   // 登录失败与锁定触发也应留痕...
#   var failAuditCount ... (使用 verifyContext)
#   ...
#   // 解锁动作必须落审计日志...
#   var audit = ...
# 因此 verifyContext 必须在 unlock 之前声明。

# 第一步：把“unlock 前插入的断言块”整体移到 unlock 之前
assert_block_start = "        // 登录失败与锁定触发也应留痕"
unlock_line = "        using var unlockResponse = await adminClient.PostAsync("
i_assert = data.find(assert_block_start)
i_unlock = data.find(unlock_line)
assert i_assert > 0 and i_unlock > 0, "markers"

# 找断言块结束（到 lockoutTriggered 断言行尾）
i_assert_end = data.find('Assert.Equal("LOCKED", lockoutTriggered.Result);', i_assert)
i_assert_end = data.find(nl, i_assert_end) + len(nl)

assert_block = data[i_assert:i_assert_end]

# 从原位置移除
data = data[:i_assert] + data[i_assert_end:]

# 重新定位 unlock 行（移除后位置变了）
i_unlock = data.find(unlock_line)
# 回退到该行行首（含缩进）
line_start = data.rfind(nl, 0, i_unlock) + len(nl)

data = data[:line_start] + assert_block + data[line_start:]

# 第二步：把 verifyContext 声明移到 unlock 之前（原来它在 unlock 后面的旧审计段里）
# 从旧审计段删除 verifyContext 声明，并在 unlock 前声明
old_decl = (
    "        // 解锁动作必须落审计日志（操作者 + 被解锁账号），可追溯绕过锁定的运维行为。" + nl
    + "        await using var verifyContext = database.CreateContext();" + nl
    + "        var audit = await verifyContext.AuditLogs"
)
new_decl = (
    "        // 解锁动作必须落审计日志（操作者 + 被解锁账号），可追溯绕过锁定的运维行为。" + nl
    + "        var audit = await verifyContext.AuditLogs"
)
assert old_decl in data, "old decl"
data = data.replace(old_decl, new_decl, 1)

open(f, "wb").write(data.encode("utf-8"))
print("reordered")
