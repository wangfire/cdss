# -*- coding: utf-8 -*-
"""打印全量硬编码清单。"""
import io
import json
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

data = json.load(open(r"logs\_i18n_full_1031.json", encoding="utf-8"))
for r in data:
    print(f"{r['file']}:{r['line']} | {r['text']}")
