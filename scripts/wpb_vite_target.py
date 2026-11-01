# -*- coding: utf-8 -*-
"""vite.config.ts 默认 proxy target 改为 5081。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

p = r"web-ui\apps\admin\vite.config.ts"
c = open(p, encoding="utf-8").read()
old = "process.env.VITE_API_PROXY_TARGET || 'http://localhost:5080'"
new = "process.env.VITE_API_PROXY_TARGET || 'http://localhost:5081'"
assert old in c, "target anchor"
c = c.replace(old, new, 1)
open(p, "w", encoding="utf-8").write(c)
print("default target 5081")
