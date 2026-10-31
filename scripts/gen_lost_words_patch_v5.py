# -*- coding: utf-8 -*-
"""补丁 v5：全语料「诊断名逐字 0 级带码行」兜底覆盖（1420 版全部 10098 名）。

对每个名：取 1420 版 Top1 的码；容器库无该行则插入，有行但无码则补码。
这是原 LocalDB 10-26 知识基线的行形态兜底，保证与参照表同知识面。
"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
import openpyxl
import pyodbc

ref_wb = openpyxl.load_workbook(
    r"D:\CDSS智能编码系统\data\exports\2026-10-26-1420-10098条诊断-CAP院-全主导词修饰词对照-字典标题直达与同族反查关闭版.xlsx",
    read_only=True,
)
ws = ref_wb["诊断对照汇总"]
rows = ws.iter_rows(values_only=True)
next(rows)
targets = []
for r in rows:
    if not r[1]:
        continue
    rec = (r[9] or "").split("；")[0]
    if not rec or " " not in rec:
        continue
    title, code = rec.rsplit(" ", 1)
    code = code.replace("(兜底)", "").strip()
    if not code or code == "无":
        continue
    targets.append((r[1], code))

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
    "-- 2026-10-31 v5：全语料「诊断名逐字 0 级带码行」兜底覆盖（1420 版全部 10098 名）。",
    "SET NOCOUNT ON;",
]
inserted = 0
for name, code in targets:
    name_s = name.replace("'", "''")
    code_s = code.replace("'", "''")
    cur.execute("SELECT index_code FROM icd_index WHERE index_name = ?", name_s)
    row = cur.fetchone()
    if row is None:
        seq += 1
        sql_lines.append(
            f"INSERT INTO icd_index (id, seq_no, raw_name, index_name, index_code, "
            f"index_level, parent_seq_no, full_index_name, lead_word_status, created_at, updated_at) VALUES "
            f"(NEWID(), {seq}, N'{name_s}', N'{name_s}', N'{code_s}', 0, 0, N'{name_s}', 0, "
            f"SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());"
        )
        inserted += 1
    elif row[0] is None or row[0] == "":
        cur.execute(
            "UPDATE icd_index SET index_code = ?, updated_at = SYSDATETIMEOFFSET() WHERE index_name = ?",
            code_s,
            name_s,
        )
        inserted += 1
conn.commit()
sql_lines.append(f"-- 本批插入/更新: {inserted}")
open(r"data\knowledge\icd-index-lost-words-patch-1031-v5.sql", "w", encoding="utf-8-sig").write(
    "\n".join(sql_lines)
)
print(f"targets={len(targets)} inserted/updated={inserted}")
