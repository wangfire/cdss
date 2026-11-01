# -*- coding: utf-8 -*-
"""WP-D i18n 收口：全量扫描 vue 模板/脚本中的硬编码中文（排除注释/已有 t()/console 等）。"""
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

# 中文（含全角标点）
CJK = r"[\u4e00-\u9fa5\u3000-\u303f\uff00-\uffef]"

# 排除：已是 t('...') 调用、注释行、import 行、纯符号
results = []
for d in dirs:
    for root, _, files in os.walk(d):
        for fn in files:
            if not fn.endswith(".vue"):
                continue
            path = os.path.join(root, fn)
            rel = os.path.relpath(path, r"web-ui\apps\admin\src")
            lines = open(path, encoding="utf-8").read().splitlines()
            in_html_comment = False
            in_js_block_comment = False
            for i, line in enumerate(lines, 1):
                stripped = line.strip()
                # HTML 注释块
                if "<!--" in stripped:
                    in_html_comment = True
                if in_html_comment:
                    if "-->" in stripped:
                        in_html_comment = False
                    continue
                # JS 块注释
                if in_js_block_comment:
                    if "*/" in stripped:
                        in_js_block_comment = False
                    continue
                if stripped.startswith("//"):
                    continue
                if "/*" in stripped:
                    in_js_block_comment = True
                    continue
                # 已是 t() 调用
                if "t('" in line or 't("' in line:
                    continue
                # console/error/log
                if "console." in line:
                    continue
                # import/路由 meta 等
                if stripped.startswith("import "):
                    continue
                # i18n key 定义（locale 文件不在扫描范围）
                m = re.search(CJK, line)
                if m:
                    # 提取中文上下文片段
                    ctx = stripped[:120]
                    results.append(
                        {"file": rel, "line": i, "text": ctx}
                    )

print(f"total hardcoded CJK lines: {len(results)}")
by_file = {}
for r in results:
    by_file.setdefault(r["file"], []).append(r)
for f, items in sorted(by_file.items(), key=lambda kv: -len(kv[1])):
    print(f"  {f}: {len(items)}")

json.dump(
    results, open(r"logs\_i18n_full_1031.json", "w", encoding="utf-8"), ensure_ascii=False, indent=1
)
