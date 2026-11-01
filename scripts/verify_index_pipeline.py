# -*- coding: utf-8 -*-
"""索引口径端到端验证 v4：兼容字段名差异，输出推荐与通道分布。"""
import io
import json
import sys
import time
import urllib.error
import urllib.request

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

BASE = "http://localhost:3001"
HOSPITAL = "ff840089-ea7d-4c40-bb4c-5d47b4b49956"
STAMP = str(int(time.time()))[-6:]


def http_json(path, payload=None, token=None):
    req = urllib.request.Request(BASE + path)
    req.add_header("Content-Type", "application/json; charset=utf-8")
    if token:
        req.add_header("Authorization", f"Bearer {token}")
    data = json.dumps(payload, ensure_ascii=False).encode("utf-8") if payload is not None else None
    try:
        return json.loads(urllib.request.urlopen(req, data=data, timeout=120).read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        body = e.read().decode("utf-8", errors="replace")[:300]
        raise RuntimeError(f"HTTP {e.code} {body}") from e


def field(obj, *names):
    for name in names:
        if name in obj and obj[name] is not None:
            return obj[name]
    return "-"


login = http_json(
    "/api/v1/auth/login",
    {"userName": "admin", "password": "admin123", "hospitalId": HOSPITAL},
)
token = login["data"]["token"]
print(f"login ok perms={len(login['data']['permissions'])}")

case = {
    "patientName": f"索引口径验证-{STAMP}",
    "medicalRecordNo": f"IDXTEST-{STAMP}",
    "admissionAt": "2026-10-20T08:00:00Z",
    "dischargeAt": "2026-10-25T08:00:00Z",
    "admissionDiagnoses": ["急性阑尾炎"],
    "dischargeDiagnoses": ["急性阑尾炎"],
    "procedures": ["阑尾切除术"],
    "additionalDocumentContent": (
        "患者因转移性右下腹痛伴发热入院，查体麦氏点压痛阳性。"
        "术中见阑尾充血水肿，行腹腔镜阑尾切除术，术后病理示急性化脓性阑尾炎。术后恢复良好出院。"
    ),
}
resp = http_json("/api/v1/coding/case-entry", case, token=token)
tid = resp["data"]["codingTaskId"]
print(f"task: {tid}")

status = ""
deadline = time.time() + 150
while time.time() < deadline:
    time.sleep(6)
    task = http_json(f"/api/v1/coding-tasks/{tid}", token=token)
    status = task["data"]["status"]
    if status in {"PENDING_REVIEW", "HUMAN_REQUIRED", "ACCEPTED", "MODIFIED", "REJECTED", "FAILED"}:
        break
print(f"status: {status}")

recs = http_json(f"/api/v1/coding-tasks/{tid}/recommendations", token=token)["data"]["recommendations"]
print(f"recommendations: {len(recs)}")
if recs:
    print("first rec keys:", list(recs[0].keys()))
for r in recs:
    code = field(r, "code")
    title = field(r, "title")
    channel = field(r, "recallChannel", "recall_channel")
    conf = field(r, "confidenceScore", "confidence_score")
    rtype = field(r, "recommendationType", "recommendation_type")
    print(f"  {code} {title} [channel={channel} conf={conf} type={rtype}]")
channels = {}
for r in recs:
    ch = field(r, "recallChannel", "recall_channel")
    channels[ch] = channels.get(ch, 0) + 1
print("channels:", channels)
