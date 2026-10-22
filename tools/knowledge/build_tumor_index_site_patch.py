# -*- coding: utf-8 -*-
"""把 tumor_index 部位词面补丁 TSV 转成幂等导入 SQL（按 seq_no 区间全删全插）。

来源: tools/knowledge/tumor_index_site_patches.tsv（`#` 开头是注释，列序同
      tumor_index 导出查询，空列或 `-` = 无码）。
输出: data/knowledge/tumor-index-site-patch.sql（UTF-8 BOM，供 sqlcmd -i 使用）

为什么单独一个补丁文件而不是改 xlsx/主 seed：主 seed 是《肿瘤索引.xlsx》的全量镜像，
补丁是**本系统实测出来的表侧词面缺口**（D 桶），混进 vendor 原表会让下次重导说不清来源。
`tumor_index` 会被主 seed 全删全插，所以补丁必须在主 seed 之后执行；回滚=删掉这个区间。
"""
import io
import sys
from pathlib import Path

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "tools/knowledge/tumor_index_site_patches.tsv"
OUT = ROOT / "data/knowledge/tumor-index-site-patch.sql"

COLUMNS = ("id, seq_no, index_name, index_name_en, index_level, parent_seq_no, full_index_name, "
           "code_malignant_primary, code_malignant_secondary, code_in_situ, code_benign, "
           "code_uncertain, jump_kind, jump_target, created_at, updated_at")


def sql_str(value):
    if value is None or value in ("", "-"):
        return "NULL"
    return "N'" + value.replace("'", "''") + "'"


def main() -> int:
    records = []
    for line in SOURCE.read_text(encoding="utf-8").splitlines():
        if not line.strip() or line.startswith("#"):
            continue
        f = [x.strip() for x in line.split("\t")]
        if len(f) < 5:
            return f"字段数不足: {line}"
        codes = [(f[i] if i < len(f) and f[i] != "-" else None) for i in range(5, 10)]
        jump_kind = int(f[10]) if len(f) > 10 and f[10] not in ("", "-") else 0
        jump_target = f[11] if len(f) > 11 and f[11] not in ("", "-") else None
        records.append((int(f[0]), f[1], int(f[2]), int(f[3]), f[4], codes, jump_kind, jump_target))

    problems = []
    seqs = [r[0] for r in records]
    if len(set(seqs)) != len(seqs):
        problems.append("seq_no 重复")
    for seq, name, level, parent, full, codes, jump_kind, jump_target in records:
        if level != 1 or parent != 1:
            problems.append(f"{seq} 不是挂在根行下的一级行（level={level} parent={parent}）")
        if not full.startswith("肿瘤，肿瘤性 "):
            problems.append(f"{seq} full_index_name 前缀不对: {full}")
        if full[len("肿瘤，肿瘤性 "):] != name.lstrip("－—–-"):
            problems.append(f"{seq} 词面与 full 不一致: {name} / {full}")
        if all(c is None for c in codes):
            problems.append(f"{seq} 五个行为列全空")
    if problems:
        for p in problems:
            print("PROBLEM:", p)
        return 1

    lo, hi = min(seqs), max(seqs)
    lines = [
        "-- tumor_index 部位词面补丁（D 桶，14 行）。生成: tools/knowledge/build_tumor_index_site_patch.py",
        "-- 必须在 data/knowledge/tumor-index-seed.sql 之后执行；回滚=删除 seq_no BETWEEN %d AND %d。" % (lo, hi),
        "SET NOCOUNT ON;",
        "DELETE FROM HospitalAi.dbo.tumor_index WHERE seq_no BETWEEN %d AND %d;" % (lo, hi),
    ]
    values = []
    for seq, name, level, parent, full, codes, jump_kind, jump_target in records:
        values.append(
            "(NEWID(), {seq}, {name}, NULL, {lv}, {parent}, {full}, {c0}, {c1}, {c2}, {c3}, {c4}, "
            "{jk}, {jt}, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET())".format(
                seq=seq, name=sql_str(name), lv=level, parent=parent, full=sql_str(full),
                c0=sql_str(codes[0]), c1=sql_str(codes[1]), c2=sql_str(codes[2]),
                c3=sql_str(codes[3]), c4=sql_str(codes[4]), jk=jump_kind, jt=sql_str(jump_target)))
    lines.append("INSERT INTO HospitalAi.dbo.tumor_index (%s) VALUES" % COLUMNS)
    lines.append(",\n".join(values) + ";")
    lines.append("SELECT COUNT(*) AS patched_rows FROM HospitalAi.dbo.tumor_index "
                 "WHERE seq_no BETWEEN %d AND %d;" % (lo, hi))
    OUT.write_bytes(b"".join([b"\xef\xbb\xbf", "\n".join(lines).encode("utf-8")]))
    print(f"rows={len(records)} seq_no=[{lo}..{hi}] -> {OUT.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
