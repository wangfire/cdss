# -*- coding: utf-8 -*-
"""WP-A 第一步：计算容器库 icd_index 与仓库批次的差集，导出增量行为规范批次 JSON。"""
import io
import json
import os
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
import pyodbc

batch_dir = r"data\knowledge\icd-index-batches"
batch_seqs = set()
sample_shape = None
for f in sorted(os.listdir(batch_dir)):
    if not f.endswith(".json"):
        continue
    data = json.load(open(os.path.join(batch_dir, f), encoding="utf-8"))
    if isinstance(data, dict):
        # 可能是 {rows: [...]} 或 {batches: [...]}
        for key in ("rows", "batches", "items", "data"):
            if key in data:
                data = data[key]
                break
    if isinstance(data, dict):
        sample_shape = sample_shape or f"{f}: dict keys={list(data)[:5]}"
        continue
    for row in data:
        if isinstance(row, dict) and "seqNo" in row:
            batch_seqs.add(row["seqNo"])
print("batch seq count:", len(batch_seqs))
if sample_shape:
    print("non-list shape found:", sample_shape)

conn = pyodbc.connect(
    "Driver={ODBC Driver 18 for SQL Server};"
    "Server=localhost,14333;Database=HospitalAi;"
    "UID=sa;PWD=ChangeMe_1234!;"
    "TrustServerCertificate=yes;Encrypt=no;",
    timeout=60,
)
cur = conn.cursor()
cur.execute("SELECT COUNT(1), MIN(seq_no), MAX(seq_no) FROM icd_index")
total, mn, mx = cur.fetchone()
print(f"db rows={total} seq range={mn}..{mx}")
cur.execute("SELECT seq_no FROM icd_index")
db_seqs = {r[0] for r in cur.fetchall()}
extra = sorted(db_seqs - batch_seqs)
missing = sorted(batch_seqs - db_seqs)
print("库内有、批次无（增量行）:", len(extra))
print("批次有、库内无（缺失行）:", len(missing))
json.dump(extra, open(r"logs\_extra_seqs_1031.json", "w"))
json.dump(missing, open(r"logs\_missing_seqs_1031.json", "w"))
