# -*- coding: utf-8 -*-
"""检查流失链路主导词行在容器库的存在性，并生成补索引行补丁 SQL。"""
import io
import json
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
import pyodbc

rows = json.load(open(r"logs\_lost_leads_core_1031.json", encoding="utf-8"))
conn = pyodbc.connect(
    "Driver={ODBC Driver 18 for SQL Server};"
    "Server=localhost,14333;Database=HospitalAi;"
    "UID=sa;PWD=ChangeMe_1234!;"
    "TrustServerCertificate=yes;Encrypt=no;",
    timeout=30,
)
cur = conn.cursor()

missing = []
for r in rows:
    lead = r["lead"].replace("'", "''")
    cur.execute(
        "SELECT COUNT(1) FROM icd_index WHERE index_name = ?", lead
    )
    cnt = cur.fetchone()[0]
    if cnt == 0:
        missing.append(r)

print(f"leads={len(rows)} missing_in_container={len(missing)}")

# 生成补丁：每个缺失主导词一行 0 级行（挂链路终点码），max(seq_no)+1 起。
cur.execute("SELECT MAX(seq_no) FROM icd_index")
max_seq = cur.fetchone()[0] or 0

lines = [
    "-- 2026-10-31 对比修复：找回 1420 版知识基线的仓库外增量词面行（27 名流失 Top1 的主导词行）。",
    "-- 来源：1420 版工作簿「主导词修饰词明细」链路记录（LocalDB 原库物理文件已丢，按台账重建）。",
    "SET NOCOUNT ON;",
]
seq = max_seq
for r in missing:
    seq += 1
    lead = r["lead"].replace("'", "''")
    code = (r["final"] if r["final"] not in ("", "0", None) else r["lead_code"] or "").strip()
    if not code or code == "无":
        code = (r["lead_code"] or "").strip()
    code_sql = "NULL" if not code else f"N'{code}'"
    lines.append(
        f"INSERT INTO icd_index (seq_no, raw_name, raw_name_en, index_name, index_code, "
        f"index_level, parent_seq_no, full_index_name, lead_word_status) VALUES "
        f"({seq}, N'{lead}', NULL, N'{lead}', {code_sql}, 0, 0, N'{lead}', 0);"
    )

lines.append(f"-- 补丁行数: {len(missing)}")
open(r"data\knowledge\icd-index-lost-words-patch-1031.sql", "w", encoding="utf-8-sig").write(
    "\n".join(lines)
)
print(f"patch written: data/knowledge/icd-index-lost-words-patch-1031.sql rows={len(missing)}")
