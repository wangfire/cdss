# -*- coding: utf-8 -*-
"""从 1420 版明细重建流失名的「库内增量行」补丁 v2。

对每个流失名：
  - Top1 链路的 0 级主导词行若在容器库不存在 → 补 0 级行（挂链路终点码）；
  - 链路终点行（final_code 对应修饰词行）若不存在 → 补子行（挂在主导词行下）。
码值优先级：链路终点编码 > 修饰词编码 > 主导词编码；去尾部 (fb) 标记。
"""
import io
import json
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

per_name = {}
for r in rows:
    if r[1] not in lost_names or r[6] != "1":
        continue
    if "有编码" not in (r[12] or ""):
        continue
    info = per_name.setdefault(
        r[1],
        {"lead": r[7], "lead_code": r[8] or "", "final_code": r[15] or "", "path": r[13] or ""},
    )

conn = pyodbc.connect(
    "Driver={ODBC Driver 18 for SQL Server};"
    "Server=localhost,14333;Database=HospitalAi;"
    "UID=sa;PWD=ChangeMe_1234!;"
    "TrustServerCertificate=yes;Encrypt=no;",
    timeout=30,
)
cur = conn.cursor()


def lead_exists(name):
    cur.execute("SELECT COUNT(1) FROM icd_index WHERE index_name = ?", name)
    return cur.fetchone()[0] > 0


def clean(code):
    return (code or "").replace("(fb)", "").strip()


sql_lines = [
    "-- 2026-10-31 对比修复 v2：重建 1420 版知识基线的仓库外增量行（27 名流失 Top1）。",
    "-- 每个流失名补两行：0 级主导词行（挂终点码）+ 修饰词子行（若路径非空且与主导词不同名）。",
    "SET NOCOUNT ON;",
]
seq = 0
cur.execute("SELECT ISNULL(MAX(seq_no), 0) FROM icd_index")
max_seq = cur.fetchone()[0]
patched = 0
for name, info in sorted(per_name.items()):
    final = clean(info["final_code"])
    lead_code = clean(info["lead_code"])
    code = final or lead_code
    lead = info["lead"].replace("'", "''")
    if not code or code == "无":
        continue
    statements = []
    if not lead_exists(info["lead"]):
        max_seq += 1
        statements.append(
            f"INSERT INTO icd_index (seq_no, raw_name, raw_name_en, index_name, index_code, "
            f"index_level, parent_seq_no, full_index_name, lead_word_status) VALUES "
            f"({max_seq}, N'{lead}', NULL, N'{lead}', N'{code}', 0, 0, N'{lead}', 0);"
        )
        patched += 1
    # 修饰词子行：路径存在且路径名 != 主导词时补 1 级行
    path = info["path"]
    if path and path not in ("—", "－") and path != info["lead"]:
        sub = path.split("→")[-1].strip().replace("'", "''")
        if sub and sub != lead:
            max_seq += 1
            statements.append(
                f"INSERT INTO icd_index (seq_no, raw_name, raw_name_en, index_name, index_code, "
                f"index_level, parent_seq_no, full_index_name, lead_word_status) VALUES "
                f"({max_seq}, N'－{sub}', NULL, N'－{sub}', N'{final or code}', 1, "
                f"(SELECT MIN(seq_no) FROM icd_index WHERE index_name = N'{lead}'), "
                f"N'{lead} {sub}', 0);"
            )
            patched += 1
    sql_lines.extend(statements)

sql_lines.append(f"-- 补丁语句数: {patched}")
open(r"data\knowledge\icd-index-lost-words-patch-1031-v2.sql", "w", encoding="utf-8-sig").write(
    "\n".join(sql_lines)
)
print(f"names={len(per_name)} statements={patched}")
