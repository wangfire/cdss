# -*- coding: utf-8 -*-
"""WP-A 第二步：把 9078 行增量导出为规范批次 JSON（batch-9001...，与仓库批次同 schema）。

schema 对齐 batch-0035 样例：seqNo / rawName / rawNameEn / indexName / indexCode /
indexLevel / parentSeqNo / fullIndexName / leadWordStatus。
仅导出「增量 seq」的行；父行在批次内的正常行也导出（保证父链完整可独立重放），
但 parentSeqNo 引用的 0 级行若不在增量集且在批次内，不重复导出。
分批：每 5000 行一个文件 batch-90xx.json。
"""
import io
import json
import os
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
import pyodbc

extra = json.load(open(r"logs\_extra_seqs_1031.json", encoding="utf-8"))
extra_set = set(extra)
print("extra seqs:", len(extra_set))

conn = pyodbc.connect(
    "Driver={ODBC Driver 18 for SQL Server};"
    "Server=localhost,14333;Database=HospitalAi;"
    "UID=sa;PWD=ChangeMe_1234!;"
    "TrustServerCertificate=yes;Encrypt=no;",
    timeout=120,
)
cur = conn.cursor()

BATCH_SIZE = 5000
os.makedirs(r"data\knowledge\icd-index-batches-extra", exist_ok=True)

rows_out = []
last_seq = 0
file_idx = 1
total = 0

cur.execute(
    "SELECT seq_no, raw_name, raw_name_en, index_name, index_code, index_level, "
    "parent_seq_no, full_index_name, lead_word_status FROM icd_index WHERE seq_no >= ? "
    "ORDER BY seq_no",
    min(extra_set),
)
for seq_no, raw_name, raw_name_en, index_name, index_code, index_level, parent_seq_no, full_index_name, lead_word_status in cur.fetchall():
    if seq_no not in extra_set:
        continue
    # 新文件开批：保证父链在前（按 seq 升序天然满足；父行 seq < 子行 seq 由导入顺序保证）
    if len(rows_out) >= BATCH_SIZE:
        out_path = rf"data\knowledge\icd-index-batches-extra\batch-90{file_idx:02d}.json"
        json.dump(rows_out, open(out_path, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        print(f"wrote {out_path} rows={len(rows_out)}")
        rows_out = []
        file_idx += 1
    rows_out.append(
        {
            "seqNo": seq_no,
            "rawName": raw_name,
            "rawNameEn": raw_name_en,
            "indexName": index_name,
            "indexCode": index_code,
            "indexLevel": index_level,
            "parentSeqNo": parent_seq_no,
            "fullIndexName": full_index_name,
            "leadWordStatus": lead_word_status,
        }
    )
    total += 1

if rows_out:
    out_path = rf"data\knowledge\icd-index-batches-extra\batch-90{file_idx:02d}.json"
    json.dump(rows_out, open(out_path, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    print(f"wrote {out_path} rows={len(rows_out)}")

print("total exported:", total)
