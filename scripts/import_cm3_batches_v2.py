# -*- coding: utf-8 -*-
"""CM3 灌数 v2：响应字段是 importedCount/updatedCount。"""
import io
import glob
import json
import sys
import time
import urllib.request

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

BASE = "http://localhost:5081"
HOSPITAL = "6b427cb5-a6ba-4a8e-9b36-311c1102f20b"


def http_json(path, payload=None, token=None, method=None):
    req = urllib.request.Request(BASE + path, method=method)
    req.add_header("Content-Type", "application/json")
    if token:
        req.add_header("Authorization", f"Bearer {token}")
    data = json.dumps(payload).encode("utf-8") if payload is not None else None
    with urllib.request.urlopen(req, data=data, timeout=300) as resp:
        return json.loads(resp.read().decode("utf-8"))


login = http_json(
    "/api/v1/auth/login",
    {"userName": "admin", "password": "admin123", "hospitalId": HOSPITAL},
)
token = login["data"]["token"]

files = sorted(glob.glob(r"data\knowledge\cm3-index-batches\batch-*.json"))
print(f"batches: {len(files)}")
total_imported = 0
total_updated = 0
start = time.time()
for idx, fp in enumerate(files, 1):
    batch = json.load(open(fp, encoding="utf-8"))
    items = batch["items"] if isinstance(batch, dict) and "items" in batch else batch
    try:
        resp = http_json("/api/v1/cm3-index/import", {"items": items}, token=token)
        total_imported += resp["data"]["importedCount"]
        total_updated += resp["data"]["updatedCount"]
        if idx % 5 == 0 or idx == len(files):
            print(f"  [{idx}/{len(files)}] imported={total_imported} updated={total_updated}")
    except Exception as e:  # noqa: BLE001
        print(f"  [{idx}/{len(files)}] FAIL: {str(e)[:120]}")
        time.sleep(2)

elapsed = time.time() - start
print(f"done in {elapsed:.0f}s: imported={total_imported} updated={total_updated}")
