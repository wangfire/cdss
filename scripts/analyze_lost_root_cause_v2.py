# -*- coding: utf-8 -*-
"""对比流失根因分析 v2：用正确列位（15=链路终点编码、18=字典展开码）。

分类：
  A. 主导词行缺失（词面行不在容器库 icd_index）
  B. 下钻子行缺失（主导词在，修饰词子行/路径码不在）
  C. 字典展开码缺失（索引行在，但字典无该细码 → 展开落空）
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
core = []
for r in rows:
    if r[1] in lost_names and r[6] == "1" and "有编码" in (r[12] or ""):
        core.append(
            {
                "name": r[1],
                "lead": r[7],
                "lead_code": r[8] or "",
                "path": r[13] or "",
                "path_code": r[14] or "",
                "final_code": r[15] or "",
                "dict_expand": r[18] or "",
                "top1": r[20] if len(r) > 20 else "",
            }
        )

conn = pyodbc.connect(
    "Driver={ODBC Driver 18 for SQL Server};"
    "Server=localhost,14333;Database=HospitalAi;"
    "UID=sa;PWD=ChangeMe_1234!;"
    "TrustServerCertificate=yes;Encrypt=no;",
    timeout=30,
)
cur = conn.cursor()
HOSP = "6b427cb5-a6ba-4a8e-9b36-311c1102f20b"


def exists_lead(name):
    cur.execute("SELECT COUNT(1) FROM icd_index WHERE index_name = ?", name)
    return cur.fetchone()[0] > 0


def exists_code(code):
    if not code:
        return True
    cur.execute(
        "SELECT COUNT(1) FROM medical_code WHERE hospital_id=? AND code=?", HOSP, code
    )
    return cur.fetchone()[0] > 0


def exists_subrow(lead, final_code):
    # 主导词行存在时，检查其子树（或自身）能否到达 final_code
    cur.execute(
        "SELECT COUNT(1) FROM icd_index WHERE index_code = ? AND full_index_name LIKE ?",
        final_code,
        lead + "%",
    )
    return cur.fetchone()[0] > 0


report = []
cat_count = {"A_主导词行缺失": 0, "B_下钻子行缺失": 0, "C_字典码缺失": 0, "D_展开与门控差异": 0}
seen = set()
for r in core:
    if r["name"] in seen:
        continue
    seen.add(r["name"])
    final_code = (r["final_code"] or "").strip()
    lead_exists = exists_lead(r["lead"])
    sub_ok = exists_subrow(r["lead"], final_code) if (lead_exists and final_code) else False
    dict_ok = exists_code(final_code)
    if not lead_exists:
        cat = "A_主导词行缺失"
    elif not dict_ok:
        cat = "C_字典码缺失"
    elif not sub_ok:
        cat = "B_下钻子行缺失"
    else:
        cat = "D_展开与门控差异"
    cat_count[cat] += 1
    report.append(
        {
            "name": r["name"],
            "category": cat,
            "lead": r["lead"],
            "final_code": final_code,
            "dict_expand": r["dict_expand"][:80],
            "top1_ref": r["top1"],
        }
    )

json.dump(
    report, open(r"logs\_lost_root_cause_v2_1031.json", "w", encoding="utf-8"), ensure_ascii=False, indent=1
)
print(json.dumps(cat_count, ensure_ascii=False))
for r in report:
    print(f"{r['category']} | {r['name']} | lead={r['lead']} | final={r['final_code']} | dict={r['dict_expand'][:40]}")
