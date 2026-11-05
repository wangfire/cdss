# -*- coding: utf-8 -*-
"""解析全部 lost-words 补丁 SQL → (seq, raw_name, index_code) 集合，存 TSV。"""
import glob, re, os

rows = {}
for path in sorted(glob.glob(r"D:/CDSS智能编码系统/data/knowledge/icd-index-lost-words-patch*.sql")):
    with open(path, encoding="utf-8") as f:
        for line in f:
            m = re.match(r"INSERT INTO icd_index .*VALUES \(NEWID\(\), (\d+), N'([^']*)', N'[^']*', N'([^']*)', (\d+),", line)
            if m:
                seq = int(m.group(1))
                rows[seq] = (m.group(2), m.group(3), int(m.group(4)), os.path.basename(path))

print("parsed patch rows:", len(rows))
seqs = sorted(rows)
print("seq range:", seqs[0], "-", seqs[-1])
with open(r"D:/CDSS智能编码系统/logs/_patch_rows_index.tsv", "w", encoding="utf-8", newline="\n") as f:
    f.write("seq\traw_name\tindex_code\tlevel\tsrc\n")
    for seq in seqs:
        n, c, lv, src = rows[seq]
        f.write(f"{seq}\t{n}\t{c}\t{lv}\t{src}\n")
print("wrote logs/_patch_rows_index.tsv")
