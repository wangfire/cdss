# -*- coding: utf-8 -*-
"""对比流失根因分析：按 1420 版逐名明细，找当前版缺的「索引行/字典行」。

分类：
  A. 主导词行缺失（词面行不在容器库 icd_index）
  B. 下钻子行缺失（主导词在，修饰词子行不在）
  C. 字典展开码缺失（索引行在、字典无该细码）
"""
import io
import json
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
import pyodbc

rows = json.load(open(r"logs\_lost_leads_core_1031.json", encoding="utf-8"))
diff = json.load(open(r"logs\_top1_diff_1031.json", encoding="utf-8"))
lost_names = set(diff["lost"])
conn = pyodbc.connect(
    "Driver={ODBC Driver 18 for SQL Server};"
    "Server=localhost,14333;Database=HospitalAi;"
    "UID=sa;PWD=ChangeMe_1234!;"
    "TrustServerCertificate=yes;Encrypt=no;",
    timeout=30,
)
cur = conn.cursor()


def exists_lead(name):
    cur.execute("SELECT COUNT(1) FROM icd_index WHERE index_name = ?", name)
    return cur.fetchone()[0] > 0


def exists_code(code):
    if not code:
        return True
    cur.execute(
        "SELECT COUNT(1) FROM medical_code WHERE hospital_id=? AND code=?",
        "6b427cb5-a6ba-4a8e-9b36-311c1102f20b",
        code,
    )
    return cur.fetchone()[0] > 0


report = []
cat_count = {"A_主导词行缺失": 0, "B_下钻子行缺失": 0, "C_字典码缺失": 0, "X_链路自身无码": 0}
seen = set()
for r in rows:
    if r["name"] not in lost_names or r["name"] in seen:
        continue
    seen.add(r["name"])
    lead_exists = exists_lead(r["lead"])
    final_code = (r["final"] or "").strip()
    lead_code = (r["lead_code"] or "").strip()
    code = final_code if final_code not in ("", "0") else lead_code
    dict_ok = exists_code(code)
    if not lead_exists:
        cat = "A_主导词行缺失"
    elif not dict_ok:
        cat = "C_字典码缺失"
    elif "有编码" in r["drill"]:
        cat = "B_下钻子行缺失"
    else:
        cat = "X_链路自身无码"
    cat_count[cat] += 1
    report.append(
        {
            "name": r["name"],
            "category": cat,
            "lead": r["lead"],
            "lead_code": lead_code,
            "final_code": final_code,
            "dict_ok": dict_ok,
        }
    )

json.dump(report, open(r"logs\_lost_root_cause_1031.json", "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print(json.dumps(cat_count, ensure_ascii=False))
for r in report:
    print(f"{r['category']} | {r['name']} | lead={r['lead']} | code={r['final_code'] or r['lead_code']}")
