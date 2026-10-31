# -*- coding: utf-8 -*-
"""WP-D：扫描用户可见高频路径硬编码中文（confirm/toast/title/placeholder）。"""
import io
import json
import os
import re
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

dirs = [
    r"web-ui\apps\admin\src\views",
    r"web-ui\apps\admin\src\components",
    r"web-ui\apps\admin\src\layouts",
]
patterns = [
    (r"(window\.confirm\([^)]*[\u4e00-\u9fa5][^)]*\))", "confirm"),
    (r"(toast\.(?:success|error|info|warning)\([^)]*[\u4e00-\u9fa5][^)]*\))", "toast"),
    (r'(title="[^"]*[\u4e00-\u9fa5][^"]*")', "title-attr"),
    (r'(placeholder="[^"]*[\u4e00-\u9fa5][^"]*")', "placeholder-attr"),
]

results = []
for d in dirs:
    for root, _, files in os.walk(d):
        for fn in files:
            if not fn.endswith(".vue"):
                continue
            path = os.path.join(root, fn)
            rel = os.path.relpath(path, r"web-ui\apps\admin\src")
            lines = open(path, encoding="utf-8").read().splitlines()
            for i, line in enumerate(lines, 1):
                stripped = line.strip()
                if stripped.startswith("//") or stripped.startswith("<!--"):
                    continue
                if "t('" in line or 't("' in line:
                    continue
                for pat, cat in patterns:
                    for m in re.finditer(pat, line):
                        results.append(
                            {"file": rel, "line": i, "category": cat, "text": m.group(1)[:100]}
                        )
                        break

print(f"hardcoded items: {len(results)}")
json.dump(
    results, open(r"logs\_i18n_hardcoded_1031.json", "w", encoding="utf-8"), ensure_ascii=False, indent=1
)
for r in results:
    print(f"  {r['file']}:{r['line']} [{r['category']}] {r['text'][:60]}")
