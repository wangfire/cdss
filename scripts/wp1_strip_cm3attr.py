# -*- coding: utf-8 -*-
"""移除剩余 cm3TitleDirect 特性。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

p = r"src\HospitalAi.Api\Program.cs"
c = open(p, encoding="utf-8-sig").read()
old = '[property: JsonPropertyName("cm3TitleDirect")] bool Cm3TitleDirect'
new = "bool Cm3TitleDirect"
n = c.count(old)
c = c.replace(old, new)
open(p, "w", encoding="utf-8-sig").write(c)
print(f"removed {n}")
