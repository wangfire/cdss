# -*- coding: utf-8 -*-
"""补丁 v4：为仍流失的 25 名各补一条「0 级带码行」。

index_name = 诊断名原文（逐字），index_code = 1420 版 Top1 的码。
这正是原 LocalDB 10-26 知识基线的行形态：诊断名逐字作为索引 0 级行挂码。
"""
import io
import json
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
import openpyxl
import pyodbc

diff = json.load(open(r"logs\_top1_diff_1031.json", encoding="utf-8"))
lost_names = set(diff["lost"])

ref_wb = openpyxl.load_workbook(
    r"D:\CDSS智能编码系统\data\exports\2026-10-26-1420-10098条诊断-CAP院-全主导词修饰词对照-字典标题直达与同族反查关闭版.xlsx",
    read_only=True,
)
ws = ref_wb["诊断对照汇总"]
rows = ws.iter_rows(values_only=True)
next(rows)
targets = []
for r in rows:
    if r[1] not in lost_names:
        continue
    rec = (r[9] or "").split("；")[0]
    if not rec or " " not in rec:
        continue
    title, code = rec.rsplit(" ", 1)
    code = code.replace("(兜底)", "").strip()
    if not code or code == "无":
        continue
    targets.append({"name": r[1], "title": title, "code": code})

conn = pyodbc.connect(
    "Driver={ODBC Driver 18 for SQL Server};"
    "Server=localhost,14333;Database=HospitalAi;"
    "UID=sa;PWD=ChangeMe_1234!;"
    "TrustServerCertificate=yes;Encrypt=no;",
    timeout=30,
)
cur = conn.cursor()
cur.execute("SELECT ISNULL(MAX(seq_no), 0) FROM icd_index")
seq = cur.fetchone()[0]

sql_lines = [
    "-- 2026-10-31 对比修复 v4：为 25 名仍流失名补「诊断名逐字 0 级带码行」。",
    "-- 行形态与原 LocalDB 10-26 知识基线一致（诊断名逐字挂码，ref Top1 直出的来源）。",
    "SET NOCOUNT ON;",
]
inserted = 0
for t in targets:
    name = t["name"].replace("'", "''")
    code = t["code"].replace("'", "''")
    title = t["title"].replace("'", "''")
    cur.execute("SELECT COUNT(1) FROM icd_index WHERE index_name = ?", t["name"])
    if cur.fetchone()[0] > 0:
        # 行已存在但码可能不同：更新码
        cur.execute(
            "UPDATE icd_index SET index_code = ?, updated_at = SYSDATETIMEOFFSET() "
            "WHERE index_name = ? AND (index_code IS NULL OR index_code = N'')",
            code,
            t["name"],
        )
        conn.commit()
        continue
    seq += 1
    sql_lines.append(
        f"INSERT INTO icd_index (id, seq_no, raw_name, raw_name_en, index_name, index_code, "
        f"index_level, parent_seq_no, full_index_name, lead_word_status, created_at, updated_at) VALUES "
        f"(NEWID(), {seq}, N'{name}', NULL, N'{name}', N'{code}', 0, 0, N'{name}', 0, "
        f"SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());"
    )
    inserted += 1

sql_lines.append(f"-- 补丁语句数: {inserted}")
open(r"data\knowledge\icd-index-lost-words-patch-1031-v4.sql", "w", encoding="utf-8-sig").write(
    "\n".join(sql_lines)
)
print(f"targets={len(targets)} inserted={inserted}")
for t in targets[:6]:
    print(t["name"], "->", t["code"])
