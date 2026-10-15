# -*- coding: utf-8 -*-
"""把《肿瘤索引.xlsx》转成 tumor_index 表的幂等导入 SQL（全删全插）。

输出: data/knowledge/tumor-index-seed.sql（UTF-8 BOM，供 sqlcmd -i 使用）
层级 = 行首全角横杠个数；父链用层级栈；见/另见指针解析进 jump_kind/jump_target。
"""
import io
import re
import sys
from pathlib import Path

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
import openpyxl

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "docs/superpowers/konwledge/肿瘤索引.xlsx"
OUT = ROOT / "data/knowledge/tumor-index-seed.sql"

DASHES = "－—–-"
JUMP_RE = re.compile(r"[-－—–\s]*(另见|见)\s*(.+)$")
EXPECTED_ROWS = 1533


def level_of(text: str) -> int:
    return len(text) - len(text.lstrip(DASHES))


def sql_str(value):
    if value is None:
        return "NULL"
    escaped = value.replace("'", "''")
    return f"N'{escaped}'"


def main() -> int:
    wb = openpyxl.load_workbook(str(SOURCE), read_only=True, data_only=True)
    rows = list(wb["Sheet1"].iter_rows(values_only=True))[2:]  # 前两行是表头

    records = []
    stack = {}  # level -> seq_no of most recent row at that level
    for index, row in enumerate(rows, start=1):
        cn = (row[0] or "").strip()
        en = (row[1] or "").strip() if row[1] else None
        if not cn:
            print(f"第 {index} 行词条为空，中止")
            return 1

        level = level_of(cn)
        core = cn.lstrip(DASHES).strip()
        jump_kind = 0
        jump_target = None
        match = JUMP_RE.search(core)
        if match:
            jump_kind = 2 if match.group(1) == "另见" else 1
            jump_target = re.sub(r"[)）◇#]+$", "", match.group(2).strip()).strip()

        codes = []
        for cell in row[2:7]:
            value = (str(cell) or "").strip().upper() if cell is not None else ""
            codes.append(value or None)

        parent = stack.get(level - 1, 0) if level > 0 else 0
        stack[level] = index
        for stale in [lv for lv in stack if lv > level]:
            del stack[stale]

        # 父链拼接：逐级向上取 core 词
        chain = [core]
        node_parent = parent
        node_level = level
        while node_parent and node_level > 0:
            prev = records[node_parent - 1]
            chain.append(prev[1])
            node_level = prev[2]
            node_parent = prev[3]
        full = " ".join(reversed(chain))
        records.append((index, core, level, parent, cn, en, codes, jump_kind, jump_target, full))

    # 校验
    problems = []
    if len(records) != EXPECTED_ROWS:
        problems.append(f"行数 {len(records)} != {EXPECTED_ROWS}")
    if sum(1 for r in records if r[2] == 0) != 1:
        problems.append("level0 行数 != 1")
    terms = {r[1] for r in records}
    fulls = {r[9] for r in records}

    def aliases_of(core: str):
        text = re.sub(r"[)）]", "", core)
        text = re.sub(r"[（(]", "，", text)
        parts = re.split(r"[或，、]", text)
        return {p.replace("NEC", "").replace("#", "").replace("◇", "").strip().rstrip("的")
                for p in parts if p.strip()}

    alias_index = {}
    for r in records:
        for alias in aliases_of(r[1]):
            if len(alias) >= 2:
                alias_index.setdefault(alias, []).append(r[0])

    def resolve(target: str):
        cleaned = target.strip().rstrip(DASHES).strip()
        cleaned = re.sub(r"[)）◇#]+$", "", cleaned).strip()
        candidates = {cleaned}
        if cleaned.startswith("肿瘤，"):
            candidates.add(cleaned[len("肿瘤，"):])
        candidates.add(cleaned.rstrip("的"))
        if candidates & terms:
            return True
        # 英语倒装 "肿瘤，神经，周围" → 词面 周围神经：逐段做别名/包含匹配
        for segment in [s for s in cleaned.split("，") if s]:
            seg = segment.strip().rstrip("的")
            if len(seg) >= 2 and (seg in alias_index or any(seg in a for a in alias_index)):
                return True
        return False

    unresolved = []
    for r in records:
        if r[7] and r[8] and not resolve(r[8]):
            unresolved.append((r[0], r[8]))
    max_full = max(len(r[9]) for r in records)
    if max_full > 300:
        problems.append(f"full_index_name 超长 {max_full}")

    OUT.parent.mkdir(parents=True, exist_ok=True)
    lines = [
        "-- tumor_index 幂等导入（全删全插）。生成: tools/knowledge/build_tumor_index_seed.py",
        "SET NOCOUNT ON;",
        "DELETE FROM HospitalAi.dbo.tumor_index;",
    ]
    batch = []
    for r in records:
        seq, core, level, parent, cn, en, codes, jk, jt, full = r
        batch.append(
            "(NEWID(), {seq}, {name}, {en}, {lv}, {parent}, {full}, "
            "{c0}, {c1}, {c2}, {c3}, {c4}, {jk}, {jt}, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET())".format(
                seq=seq,
                name=sql_str(cn),
                en=sql_str(en),
                lv=level,
                parent=parent,
                full=sql_str(full),
                c0=sql_str(codes[0]),
                c1=sql_str(codes[1]),
                c2=sql_str(codes[2]),
                c3=sql_str(codes[3]),
                c4=sql_str(codes[4]),
                jk=jk,
                jt=sql_str(jt),
            )
        )
        if len(batch) == 200:
            lines.append("INSERT INTO HospitalAi.dbo.tumor_index (id, seq_no, index_name, index_name_en, index_level, parent_seq_no, full_index_name, code_malignant_primary, code_malignant_secondary, code_in_situ, code_benign, code_uncertain, jump_kind, jump_target, created_at, updated_at) VALUES")
            lines.append(",\n".join(batch) + ";")
            batch = []
    if batch:
        lines.append("INSERT INTO HospitalAi.dbo.tumor_index (id, seq_no, index_name, index_name_en, index_level, parent_seq_no, full_index_name, code_malignant_primary, code_malignant_secondary, code_in_situ, code_benign, code_uncertain, jump_kind, jump_target, created_at, updated_at) VALUES")
        lines.append(",\n".join(batch) + ";")
    lines.append("SELECT COUNT(*) AS tumor_rows FROM HospitalAi.dbo.tumor_index;")
    OUT.write_bytes(b"".join([b"\xef\xbb\xbf", "\n".join(lines).encode("utf-8")]))

    print(f"rows={len(records)} max_full_len={max_full} unresolved_pointers={len(unresolved)}")
    for seq, target in unresolved:
        print("  悬空指针 seq", seq, "->", target)
    for p in problems:
        print("PROBLEM:", p)
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
