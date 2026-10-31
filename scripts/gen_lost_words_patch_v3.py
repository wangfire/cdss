# -*- coding: utf-8 -*-
"""补丁 v3：按 1420 版明细为 26 名流失名重建完整链路行。

对每个流失名，取 ref 明细中「生产名额内=1 且链路有码」的所有行（不只 Top1 链路），
按层级重建：
  - 0 级主导词行：index_name = 主导词，code = 该行「主导词编码」或「链路终点编码」
  - 修饰词子行：按「修饰词(下钻路径)」→ 分隔逐级补 1/2 级行，末级挂链路终点码
码清洗：去 (fb)、†→+。已存在的行（按 full_index_name 判重）跳过。
"""
import io
import json
import re
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
import openpyxl
import pyodbc

diff = json.load(open(r"logs\_top1_diff_1031.json", encoding="utf-8"))
lost_names = set(diff["lost"])

wb = openpyxl.load_workbook(
    r"D:\CDSS智能编码系统\data\exports\2026-10-26-1420-10098条诊断-CAP院-全主导词修饰词对照-字典标题直达与同族反查关闭版.xlsx",
    read_only=True,
)
ws = wb["主导词修饰词明细"]
rows = ws.iter_rows(values_only=True)
next(rows)

# 收集每个流失名的全部有码链路行
per_name = {}
for r in rows:
    if r[1] not in lost_names or r[6] != "1":
        continue
    if "有编码" not in (r[12] or ""):
        continue
    per_name.setdefault(
        r[1],
        {
            "lead": r[7],
            "lead_code": (r[8] or "").replace("(fb)", "").strip(),
            "path": r[13] or "",
            "path_code": (r[14] or "").replace("(fb)", "").strip(),
            "final_code": (r[15] or "").replace("(fb)", "").strip(),
        },
    )

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

# 已存在行集合（index_name 全集，用于判重）
cur.execute("SELECT index_name FROM icd_index")
existing = {row[0] for row in cur.fetchall()}

HOSP = "6b427cb5-a6ba-4a8e-9b36-311c1102f20b"


def clean_code(code):
    code = re.sub(r"\(fb\)", "", code or "").replace("†", "+").replace("‡", "+").strip()
    return code if code and code != "无" else ""


sql_lines = [
    "-- 2026-10-31 对比修复 v3：为 26 名流失 Top1 重建 1420 版知识基线的仓库外链路行。",
    "-- 行来源 = 1420 版工作簿明细（每流失名的全部有码链路），按主导词行 + 逐级修饰词子行重建。",
    "SET NOCOUNT ON;",
]
inserted = 0


def esc(text):
    return (text or "").replace("'", "''")


for name, info in sorted(per_name.items()):
    lead = info["lead"]
    final_code = clean_code(info["final_code"])
    lead_code = clean_code(info["lead_code"])
    path = info["path"]
    levels = [p.strip() for p in path.split("→") if p.strip() and p.strip() not in ("—", "－")] if path else []

    # 找父行 seq：0 级行；不存在则先建
    cur.execute(
        "SELECT MIN(seq_no) FROM icd_index WHERE index_name = ?", lead
    )
    parent_seq = cur.fetchone()[0]
    if parent_seq is None:
        seq += 1
        parent_seq = seq
        code0 = final_code or lead_code
        sql_lines.append(
            f"INSERT INTO icd_index (id, seq_no, raw_name, raw_name_en, index_name, index_code, "
            f"index_level, parent_seq_no, full_index_name, lead_word_status, created_at, updated_at) VALUES "
            f"(NEWID(), {seq}, N'{esc(lead)}', NULL, N'{esc(lead)}', N'{esc(code0)}', 0, 0, "
            f"N'{esc(lead)}', 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());"
        )
        inserted += 1
        existing.add(lead)

    # 逐级修饰词子行
    full = lead
    prev_seq = parent_seq
    for depth, level_name in enumerate(levels, start=1):
        full = f"{full} {level_name}"
        key = f"－{'－' * (depth - 1)}{level_name}"
        if key in existing and depth < len(levels):
            # 已存在：查它的 seq 作为下一级父
            cur.execute(
                "SELECT MIN(seq_no) FROM icd_index WHERE index_name = ? AND parent_seq_no = ?",
                key,
                prev_seq,
            )
            found = cur.fetchone()[0]
            if found:
                prev_seq = found
                continue
        if depth == len(levels):
            # 末级：挂终点码
            code = final_code or lead_code
        else:
            # 中间级：从 path_code 里取第 depth 段（; 分隔），取不到就 NULL
            segs = [s.strip() for s in (info["path_code"] or "").split(";") if s.strip()]
            code = segs[depth - 1] if depth - 1 < len(segs) else ""
        seq += 1
        code_sql = f"N'{esc(clean_code(code))}'" if clean_code(code) else "NULL"
        sql_lines.append(
            f"INSERT INTO icd_index (id, seq_no, raw_name, raw_name_en, index_name, index_code, "
            f"index_level, parent_seq_no, full_index_name, lead_word_status, created_at, updated_at) VALUES "
            f"(NEWID(), {seq}, N'{esc(key)}', NULL, N'{esc(key)}', {code_sql}, {depth}, {prev_seq}, "
            f"N'{esc(full)}', 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());"
        )
        inserted += 1
        existing.add(key)
        prev_seq = seq

sql_lines.append(f"-- 补丁语句数: {inserted}")
open(r"data\knowledge\icd-index-lost-words-patch-1031-v3.sql", "w", encoding="utf-8-sig").write(
    "\n".join(sql_lines)
)
print(f"names={len(per_name)} inserted_statements={inserted}")
