# -*- coding: utf-8 -*-
"""登录调试：打印 400 响应体。"""
import io
import json
import sys
import urllib.error
import urllib.request

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

BASE = "http://localhost:5081"
req = urllib.request.Request(BASE + "/api/v1/auth/login", method="POST")
req.add_header("Content-Type", "application/json; charset=utf-8")
data = json.dumps(
    {"userName": "admin", "password": "admin123", "hospitalId": "ff840089-ea7d-4c40-bb4c-5d47b4b49956"}
).encode("utf-8")
try:
    resp = urllib.request.urlopen(req, data=data, timeout=60)
    print(resp.read().decode("utf-8")[:300])
except urllib.error.HTTPError as e:
    print("HTTP", e.code, e.read().decode("utf-8", errors="replace")[:300])
