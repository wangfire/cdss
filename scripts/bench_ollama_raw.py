# -*- coding: utf-8 -*-
"""隔离测 Ollama 生成延迟（排除数据库/网络/流水线干扰）。"""
import io
import json
import sys
import time
import urllib.request

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

body = json.dumps({
    "model": "qwen2.5:3b-instruct",
    "stream": False, "raw": False,
    "options": {"num_predict": 64},
    "messages": [{"role": "user", "content": "用一句话介绍阑尾炎"}],
}).encode()

for i in range(3):
    req = urllib.request.Request(
        "http://localhost:11434/api/chat", data=body,
        headers={"Content-Type": "application/json"})
    start = time.perf_counter()
    with urllib.request.urlopen(req, timeout=120) as r:
        p = json.loads(r.read().decode())
    elapsed = time.perf_counter() - start
    td = p.get("total_duration", 0) / 1e9
    ld = p.get("load_duration", 0) / 1e9
    pe = p.get("prompt_eval_duration", 0) / 1e9
    ev = p.get("eval_duration", 0) / 1e9
    other = td - ld - pe - ev
    print(f"run{i+1}: total={td:.2f}s (load={ld:.2f} prompt={pe:.2f} "
          f"gen={ev:.2f} other={other:.2f})")
