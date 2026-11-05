# -*- coding: utf-8 -*-
"""修正端点引用：AdminUserManage → UserManage；补 Worker.Pipeline / Outbox 命名空间 using。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

p = r"src\HospitalAi.Api\Program.cs"
c = open(p, encoding="utf-8-sig").read()

# 1. 权限常量
n1 = c.count("AppPermissions.AdminUserManage")
c = c.replace("AppPermissions.AdminUserManage", "AppPermissions.UserManage")

# 2. using 补充（V22PipelineOptions 在 HospitalAi.Worker.Pipeline；OutboxStore 在 HospitalAi.Infrastructure.Outbox）
usings_needed = [
    "using HospitalAi.Worker.Pipeline;",
    "using HospitalAi.Infrastructure.Outbox;",
]
first_using_anchor = "using HospitalAi.Application.Security;"
# 找第一个 using 行位置插入
import re
m = re.search(r"^using .+;$", c, re.MULTILINE)
insert_after = m.group(0)
add_lines = "".join(f"\n{u}" for u in usings_needed if u not in c)
if add_lines:
    c = c.replace(insert_after, insert_after + add_lines, 1)

open(p, "w", encoding="utf-8-sig").write(c)
print(f"perm refs fixed={n1}, usings added={bool(add_lines)}")
