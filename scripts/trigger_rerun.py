# -*- coding: utf-8 -*-
"""触发重跑（新 API 正常登录后），并轮询任务执行状态。"""
import io
import json
import sys
import time
import urllib.error
import urllib.request

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

BASE = "http://localhost:5081"
HOSPITAL = "ff840089-ea7d-4c40-bb4c-5d47b4b49956"


def http_json(path, payload=None, token=None, method=None, timeout=600):
    req = urllib.request.Request(BASE + path, method=method)
    req.add_header("Content-Type", "application/json; charset=utf-8")
    if token:
        req.add_header("Authorization", f"Bearer {token}")
    data = json.dumps(payload, ensure_ascii=False).encode("utf-8") if payload is not None else None
    with urllib.request.urlopen(req, data=data, timeout=timeout) as resp:
        return json.loads(resp.read().decode("utf-8"))


login = http_json(
    "/api/v1/auth/login",
    {"userName": "admin", "password": "admin123", "hospitalId": HOSPITAL},
    method="POST",
)
token = login["data"]["token"]
print("login ok")

try:
    rerun = http_json("/api/v1/admin/pipeline/rerun", {}, token=token, method="POST")
    print(f"rerun response: {json.dumps(rerun, ensure_ascii=False)[:200]}")
except urllib.error.HTTPError as e:
    print(f"rerun HTTP {e.code}: {e.read().decode('utf-8', errors='replace')[:300]}")
    sys.exit(1)
