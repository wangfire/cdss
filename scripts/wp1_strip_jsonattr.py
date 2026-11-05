# -*- coding: utf-8 -*-
"""移除 API 端 record 的 JsonPropertyName 特性（Program.cs 无该 using，默认 camelCase 即可）。"""
import io
import re
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

p = r"src\HospitalAi.Api\Program.cs"
c = open(p, encoding="utf-8-sig").read()

pattern = re.compile(r'\[property: JsonPropertyName\("[A-Za-z]+"\)\]\s*')
c2, n = pattern.subn("", c)
open(p, "w", encoding="utf-8-sig").write(c2)
print(f"removed {n} attributes")
