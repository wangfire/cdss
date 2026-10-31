# -*- coding: utf-8 -*-
"""清洗补丁 v3 的非法码值并去重。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

p = r"data\knowledge\icd-index-lost-words-patch-1031-v3.sql"
lines = open(p, encoding="utf-8-sig").read().splitlines()
seen = set()
out = []
for line in lines:
    if line.startswith("INSERT"):
        if line in seen:
            continue
        seen.add(line)
        # 非法码值：含 → 或 无 的中间级码段置 NULL
        line = line.replace("N'N05.-→N01.-'", "NULL").replace("N'N05.-→无'", "NULL")
    out.append(line)
open(p, "w", encoding="utf-8-sig").write("\n".join(out))
print("cleaned, inserts:", len(seen))
