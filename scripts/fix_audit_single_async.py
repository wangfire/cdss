# -*- coding: utf-8 -*-
"""修复审计断言：login_failed 有多条（每次失败一条），改用 FirstAsync / CountAsync 区分。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

f = r"tests\HospitalAi.Api.Tests\ApiSecurityCloseoutTests.cs"
data = open(f, "rb").read().decode("utf-8")
nl = "\r\n" if "\r\n" in data else "\n"

old = (
    "        Assert.Equal(\"FAILED\", (await verifyContext.AuditLogs" + nl
    + "            .SingleAsync(item => item.Action == \"app_user.login_failed\")).Result);"
)
new = (
    "        Assert.Equal(\"FAILED\", (await verifyContext.AuditLogs" + nl
    + "            .FirstAsync(item => item.Action == \"app_user.login_failed\")).Result);"
)
assert old in data, "single login_failed anchor"
data = data.replace(old, new, 1)
open(f, "wb").write(data.encode("utf-8"))
print("fixed SingleAsync -> FirstAsync for login_failed")
