# -*- coding: utf-8 -*-
"""E3 补词批次生成（WP-2 步骤 3，v5 兜底行形态）。

输入 logs/e3_classify_v2/ 四类清单，输出分批 SQL（data/knowledge/icd-index-e3-patch-1115-*.sql）：
  批次1（自动可信）：A1 字符变体 + A2 包含同源 —— 挂字典 title 反查码
  批次2（title 模糊捞回）：B 类中裸名词段在字典 title 唯一命中且 LCP≥2 —— 挂捞回码
  批次3（LCZD 众数参照）：B 类剩余 —— 挂 LCZD 众数码，说明列标人工复核
行形态与 gen_lost_words_patch_v5.py 一致（0 级带码行），seq_no 续 icd_index MAX，
同名行跳过（幂等），每批可整体回滚（按 created_at 批次注释/版本名定位）。
"""
import io
import os
import re
import sys

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
HOSPITAL = os.environ.get("E3_HOSPITAL", "ff840089-ea7d-4c40-bb4c-5d47b4b49956")
CLS_DIR = os.environ.get("E3_CLS_DIR", "logs/e3_classify_v2")
OUT_DIR = "data/knowledge"
BATCH_TAG = "e3-patch-1115"
STOP_CHARS = set("性 的 部 状 症 术 后 型 类 级 别".split())


def read_tsv(name: str) -> list:
    path = os.path.join(CLS_DIR, name)
    rows = []
    with open(path, encoding="utf-8") as handle:
        next(handle)
        for line in handle:
            parts = line.rstrip("\n").split("\t")
            if len(parts) >= 2 and parts[0].strip():
                # LCZD 众数码可能带 / 多码（I50.903/E11.401+G63.2*）：v5 口径取首码
                if len(parts) >= 2 and parts[1]:
                    parts[1] = parts[1].split("/")[0].strip()
                rows.append(parts)
    return rows


def longest_common(a: str, b: str) -> int:
    best = 0
    for i in range(len(a)):
        for j in range(len(b)):
            k = 0
            while i + k < len(a) and j + k < len(b) and a[i + k] == b[j + k]:
                k += 1
            best = max(best, k)
    return best


def main() -> None:
    conn = pyodbc.connect(MAIN_CONN, timeout=60)
    cur = conn.cursor()

    cur.execute("SELECT ISNULL(MAX(seq_no), 0) FROM icd_index")
    seq = cur.fetchone()[0]
    cur.execute("SELECT index_name FROM icd_index WHERE index_level = 0")
    existing = {row[0] for row in cur.fetchall()}

    # 字典 title 全量（模糊捞回用）
    cur.execute(
        "SELECT code, title FROM medical_code "
        "WHERE hospital_id = ? AND code_system_code = N'ICD-10'",
        HOSPITAL,
    )
    titles = [((code or "").strip(), (title or "").strip()) for code, title in cur.fetchall()]

    def fuzzy_lookup(name: str):
        """裸名词段唯一命中字典 title 且 LCP≥2 → (code, title)；否则 None。"""
        bare = re.sub(r"^[\s\-—－–0-9.、,，]+", "", name).strip()
        if len(bare) < 4:
            return None
        segments = {bare[i:i + 2] for i in range(len(bare) - 1)}
        segments = {s for s in segments if s[0] not in STOP_CHARS and s[1] not in STOP_CHARS}
        candidates: dict = {}
        for seg in segments:
            hits = [(c, t) for c, t in titles if seg in t and len(seg) >= 2]
            for c, t in hits:
                candidates.setdefault((c, t), 0)
                candidates[(c, t)] += 1
        if not candidates:
            return None
        best, score = max(candidates.items(), key=lambda kv: (kv[1], longest_common(normalize_text(kv[0][1]), normalize_text(bare))))
        if score >= 2 and longest_common(normalize_text(best[1]), normalize_text(bare)) >= 2:
            return best
        return None

    def normalize_text(text: str) -> str:
        return re.sub(r"[\s\"'（）()\[\]\-—－–]+", "", text or "")

    def esc(text: str) -> str:
        return (text or "").replace("'", "''")

    a_rows = read_tsv("A1字符变体.tsv") + read_tsv("A2包含同源.tsv")
    limit = int(os.environ.get("E3_LIMIT", "0") or 0)
    if limit > 0 and len(a_rows) > limit:
        a_rows = a_rows[:limit]
        print(f"E3_LIMIT={limit}：本批截取前 {limit} 名")
    # B 类（LCZD 语境码/模糊捞回）默认不补——第一轮实验证明语境码错位 + 兜底行
    # 挤占真链路（新增空 269，见 2026-11-15 实验报告 §三）；人工复核后另行批次。
    b_rows = read_tsv("B缺行人工复核.tsv") if os.environ.get("E3_B_PATCH") == "1" else []

    batches = []
    batch1 = [(name, code, note) for name, code, _title, note in a_rows if code]
    batch2, batch3 = [], []
    for name, code, _title, note in b_rows:
        fuzzy = fuzzy_lookup(name)
        if fuzzy:
            batch2.append((name, fuzzy[0], f"title模糊捞回 {fuzzy[1]}（原参照 {code}）"))
        elif code:
            batch3.append((name, code, note))
        else:
            batch3.append((name, code, note or "无参照码，人工定"))

    batches.append(("batch1-title反查", batch1))
    batches.append(("batch2-模糊捞回", batch2))
    batches.append(("batch3-lczd众数", batch3))

    total = 0
    for tag, rows in batches:
        if not rows:
            print(f"{tag}: 0 条，跳过")
            continue
        lines = [
            f"-- 2026-11-15 {BATCH_TAG} {tag}：E3 补词 {len(rows)} 条（0 级带码行，v5 兜底形态）。",
            f"-- 回滚：DELETE FROM icd_index WHERE created_at >= '2026-11-15' AND raw_name IN 本批名单；批次版本 {BATCH_TAG}-{tag}。",
            "SET NOCOUNT ON;",
        ]
        inserted = skipped = 0
        for name, code, note in rows:
            name_s = esc(name.strip())
            if not name_s or not code:
                skipped += 1
                continue
            cur.execute("SELECT index_code FROM icd_index WHERE index_name = ?", name_s)
            row = cur.fetchone()
            if row is not None:
                if not row[0]:
                    cur.execute(
                        "UPDATE icd_index SET index_code = ?, updated_at = SYSDATETIMEOFFSET() WHERE index_name = ?",
                        esc(code),
                        name_s,
                    )
                    lines.append(f"-- UPDATE 补码: {name_s} -> {esc(code)}  ({esc(note)})")
                    inserted += 1
                else:
                    skipped += 1
                    lines.append(f"-- SKIP 已有带码行: {name_s} = {row[0]}")
                continue
            seq += 1
            cur.execute(
                "INSERT INTO icd_index (id, seq_no, raw_name, index_name, index_code, "
                "index_level, parent_seq_no, full_index_name, lead_word_status, created_at, updated_at) VALUES "
                "(NEWID(), ?, ?, ?, ?, 0, 0, ?, 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());",
                seq,
                name_s,
                name_s,
                esc(code),
                name_s,
            )
            lines.append(
                f"INSERT INTO icd_index (id, seq_no, raw_name, index_name, index_code, "
                f"index_level, parent_seq_no, full_index_name, lead_word_status, created_at, updated_at) VALUES "
                f"(NEWID(), {seq}, N'{name_s}', N'{name_s}', N'{esc(code)}', 0, 0, N'{name_s}', 0, "
                f"SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()); -- {esc(note)}"
            )
            inserted += 1
        conn.commit()
        path = os.path.join(OUT_DIR, f"icd-index-{BATCH_TAG}-{tag}.sql")
        with open(path, "w", encoding="utf-8-sig") as handle:
            handle.write("\n".join(lines))
        total += inserted
        print(f"{tag}: 插入/补码 {inserted}，跳过 {skipped} → {path}")
    print(f"合计入库 {total}")


if __name__ == "__main__":
    main()
