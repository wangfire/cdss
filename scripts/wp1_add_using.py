# -*- coding: utf-8 -*-
"""补 CodingTaskStatus 命名空间 using。"""
import io
import re
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

p = r"src\HospitalAi.Api\Program.cs"
c = open(p, encoding="utf-8-sig").read()
u = "using HospitalAi.Domain.CodingTasks;"
if u not in c:
    m = re.search(r"^using .+;$", c, re.MULTILINE)
    c = c.replace(m.group(0), m.group(0) + "\n" + u, 1)
    open(p, "w", encoding="utf-8-sig").write(c)
    print("using added")
else:
    print("already present")
