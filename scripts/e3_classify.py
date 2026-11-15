# -*- coding: utf-8 -*-
"""E3 无锚点名单分诊 v2（WP-2 步骤 1 修正版）。

v1 教训：LCZD 逐名码是病历语境码（甲亢性肝病→I48.X01 与 E05.900X010+K77.8* 两行两码），
不可直接挂码。v2 以**字典 title 反查为补码唯一来源**：
  A1 字符变体同源 —— 名 ≡ 某条字典 title（罗马数字/全半角/装饰差异）→ 补 0 级带码行（title 码）
  A2 包含同源     —— 字典 title 含裸名或裸名含 title（≥4 字段防弱同源）→ 补 0 级带码行（title 码）
  B 词面缺行      —— title 反查不中，LCZD 众数码参照 + 人工复核批次
  C 临床语        —— 众数码为不可判定码 → 剔除类
输出：logs/e3_classify_v2/ 四类 TSV。只读不落库。
"""
import io
import os
import re
import sys
from collections import Counter

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
import pyodbc

MAIN_CONN = os.environ.get(
    "E3_CONN",
    "Driver={ODBC Driver 18 for SQL Server};Server=localhost,14333;Database=HospitalAi;"
    "UID=sa;PWD=ChangeMe_1234!;TrustServerCertificate=yes;Encrypt=no;",
)
if "Driver=" not in MAIN_CONN:
    MAIN_CONN = ("Driver={ODBC Driver 18 for SQL Server};" + MAIN_CONN
                 .replace("User Id=", "UID=").replace("Password=", "PWD=").replace("Password=", "PWD=")
                 .replace("TrustServerCertificate=True", "TrustServerCertificate=yes"))
REF_CONN = (
    "Driver={ODBC Driver 18 for SQL Server};Server=(localdb)\\MSSQLLocalDB;"
    "Database=WaterCloudNetDb_JZEY;Integrated Security=true;TrustServerCertificate=yes;"
)
HOSPITAL = os.environ.get("E3_HOSPITAL", "ff840089-ea7d-4c40-bb4c-5d47b4b49956")  # 默认 ICD10CAP-3B；评测库传 5074310C
E3_TSV = "docs/superpowers/reports/e3-attribution-20261114/e3-no-anchor.tsv"
OUT_DIR = os.environ.get("E3_OUT_DIR", "logs/e3_classify_v2")
NON_JUDGMENTAL_PREFIX = ("R69", "Z99", "R99", "Z00", "Z03")

VARIANT_TABLE = {"Ⅰ": "I", "Ⅱ": "II", "Ⅲ": "III", "Ⅳ": "IV", "Ⅴ": "V", "Ⅵ": "VI", "Ⅶ": "VII"}


def strip_deco(text: str) -> str:
    """去前导装饰（语料编号残留：-、–、数字点、空白）。"""
    return re.sub(r"^[\s\-—－–0-9.、,，]+", "", text or "").strip()


def normalize(text: str) -> str:
    s = strip_deco(text)
    for src, dst in VARIANT_TABLE.items():
        s = s.replace(src, dst)
    s = s.replace("（", "(").replace("）", ")")
    return re.sub(r"[\s\"'“”‘’\-—－–、，,。.]+", "", s)


def load_names() -> list[str]:
    names = []
    with open(E3_TSV, encoding="utf-8") as handle:
        for line in handle:
            parts = line.rstrip("\n").split("\t")
            if not parts or parts[0] in ("名称", ""):
                continue
            name = parts[0].strip()
            if name and name not in names:
                names.append(name)
    return names


def main() -> None:
    main_conn = pyodbc.connect(MAIN_CONN, timeout=60)
    ref_conn = pyodbc.connect(REF_CONN, timeout=30)
    mc, rc = main_conn.cursor(), ref_conn.cursor()

    # 字典（CAP-3B / ICD-10）全量：归一化 title → [(code, 原始title)]
    mc.execute(
        "SELECT code, title FROM medical_code "
        "WHERE hospital_id = ? AND code_system_code = N'ICD-10'",
        HOSPITAL,
    )
    title_index: dict[str, list] = {}
    for code, title in mc.fetchall():
        title = (title or "").strip()
        if title:
            title_index.setdefault(normalize(title), []).append(((code or "").strip(), title))

    # LCZD：归一化名 → 码众数（参照用途）
    truth_votes: dict[str, Counter] = {}
    rc.execute("SELECT Name, Code FROM DiagnosticSet_LCZD")
    for name, code in rc.fetchall():
        key = normalize(name)
        if code:
            truth_votes.setdefault(key, Counter())[(code or "").strip()] += 1

    names = load_names()
    print(f"E3 名单去重后 {len(names)} 名")

    buckets: dict[str, list] = {tag: [] for tag in ("A1字符变体", "A2包含同源", "B缺行人工复核", "C临床语", "E复合名不补")}
    for name in names:
        # 复合名过滤（第一轮新增空主力，见 2026-11-15 实验报告 §三）：序号前缀/句点多段/
        # 顿号并列的多诊断名，逐字兜底行挂单成分码必错位——不进补行，留人工。
        if re.search(r"[。；;]", name) or re.match(r"^\s*\d+[.、]", name) or re.search(r"\d+[.、]", name):
            buckets["E复合名不补"].append((name, "", "", "复合名/序号名，逐字补行不适用"))
            continue
        n_key = normalize(name)
        hit = title_index.get(n_key)
        if hit:
            code, title = hit[0]
            buckets["A1字符变体"].append((name, code, title, "title 字符变体精确匹配"))
            continue

        # A2：title 含裸名 或 裸名含 title（裸名 ≥4 字防弱同源）
        bare = strip_deco(name)
        contain_hit = None
        if len(n_key) >= 4:
            for t_key, entries in title_index.items():
                if n_key in t_key or (len(t_key) >= 4 and t_key in n_key):
                    contain_hit = entries[0]
                    break
        if contain_hit:
            code, title = contain_hit
            buckets["A2包含同源"].append((name, code, title, "字典 title 包含同源"))
            continue

        votes = truth_votes.get(n_key)
        if not votes:
            buckets["B缺行人工复核"].append((name, "", "", "LCZD 无同名（按装饰变体聚合后仍无）"))
            continue
        code, votes_n = votes.most_common(1)[0]
        base5 = re.sub(r"X\d+$", "", code)[:5]
        if any(code.startswith(p) or base5.startswith(p) for p in NON_JUDGMENTAL_PREFIX):
            buckets["C临床语"].append((name, code, "", f"众数码不可判定（{votes_n} 票）"))
        else:
            buckets["B缺行人工复核"].append((name, code, "", f"LCZD 众数码 {votes_n} 票，人工复核"))

    os.makedirs(OUT_DIR, exist_ok=True)
    for tag, rows in buckets.items():
        path = os.path.join(OUT_DIR, f"{tag}.tsv")
        with open(path, "w", encoding="utf-8", newline="") as handle:
            handle.write("名称\t补码\t字典title\t说明\n")
            for row in rows:
                handle.write("\t".join(row) + "\n")
        print(f"{tag}: {len(rows)} → {path}")
    print(f"合计 {sum(len(r) for r in buckets.values())}")


if __name__ == "__main__":
    main()
