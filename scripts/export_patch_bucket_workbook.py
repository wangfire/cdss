# -*- coding: utf-8 -*-
"""在 10-31 终版工作簿基础上按 v5 补丁行归桶，导出新版工作簿。

判据：icd_index 库内 60346 行 = 原版 51326 + lost-words 补丁 9020（seq 60059-69078，零其他高位行）。
补丁行全部是 0 级带码行，台账命中等价于 (主导词, 主导词编码) 命中补丁 (raw_name, index_code) 对。
值级复制（不保留单元格样式），新增三处：
  明细页尾列「主导词来源」：原版索引 / v5补丁行
  汇总页尾列「Top1主导词来源」：原文 rank1 行的来源；无 rank1 视为 —
  新页「补丁行分桶统计」：明细行桶分布 + 10098 名 Top1 桶分布与命中率
"""
import openpyxl
from openpyxl import Workbook

SRC = r"D:/CDSS智能编码系统/data/exports/2026-10-31-1210-10098条诊断-CAP院-全主导词修饰词对照-当前版本-终版.xlsx"
PATCH_TSV = r"D:/CDSS智能编码系统/logs/_patch_rows_index.tsv"
OUT = r"D:/CDSS智能编码系统/data/exports/2026-11-02-10098条诊断-CAP院-全主导词修饰词对照-补丁行分桶版.xlsx"

patch = set()
patch_codes = {}
with open(PATCH_TSV, encoding="utf-8") as f:
    header = f.readline().rstrip("\n").split("\t")
    i_name, i_code = header.index("raw_name"), header.index("index_code")
    for line in f:
        p = line.rstrip("\n").split("\t")
        key = (p[i_name].strip(), p[i_code].strip())
        patch.add(key)
        patch_codes.setdefault(p[i_code].strip(), set()).add(p[i_name].strip())
print("patch pairs:", len(patch))

wb = openpyxl.load_workbook(SRC, read_only=True, data_only=True)
out = Workbook()
out.remove(out.active)

# ---- Sheet1 明细 ----
src1 = wb["主导词修饰词明细"]
it = src1.iter_rows(values_only=True)
head = list(next(it))
i_lead = head.index("主导词")
i_lcode = head.index("主导词编码")
i_name1 = head.index("原始诊断")
i_rank = head.index("名次")
i_src = head.index("来源")
i_top1 = head.index("Top1")
head.append("主导词来源")
ws1 = out.create_sheet("主导词修饰词明细")
ws1.append(head)

rank1_src = {}      # name -> 来源 (原文 rank1)
rank1_hit = {}      # name -> Top1 是否命中
bucket_rows = {"原版索引": 0, "v5补丁行": 0, "—": 0}
for r in it:
    row = list(r)
    lead = str(row[i_lead]).strip() if row[i_lead] is not None else ""
    lcode = str(row[i_lcode]).strip() if row[i_lcode] is not None else ""
    if lead and lcode and (lead, lcode) in patch:
        src = "v5补丁行"
    elif lead:
        src = "原版索引"
    else:
        src = "—"
    bucket_rows[src] = bucket_rows.get(src, 0) + 1
    row.append(src)
    ws1.append(row)
    name = str(row[i_name1]).strip() if row[i_name1] is not None else ""
    if name and str(row[i_src] or "").strip() == "原文" and str(row[i_rank] or "").strip() == "1":
        rank1_src[name] = src
        rank1_hit[name] = "✓" in str(row[i_top1] or "")
print("detail rows bucket:", bucket_rows)

# ---- Sheet2 汇总 ----
src2 = wb["诊断对照汇总"]
it2 = src2.iter_rows(values_only=True)
head2 = list(next(it2))
i_name2 = head2.index("原始诊断")
head2.append("Top1主导词来源")
ws2 = out.create_sheet("诊断对照汇总")
ws2.append(head2)
top1_bucket = {"原版索引": [0, 0], "v5补丁行": [0, 0], "—": [0, 0]}  # [名字数, 其中Top1命中]
for r in it2:
    row = list(r)
    name = str(row[i_name2]).strip() if row[i_name2] is not None else ""
    src = rank1_src.get(name, "—")
    row.append(src)
    ws2.append(row)
    b = top1_bucket.setdefault(src, [0, 0])
    b[0] += 1
    if rank1_hit.get(name):
        b[1] += 1
print("names top1 bucket:", top1_bucket)

# ---- 补丁行分桶统计 ----
ws0 = out.create_sheet("补丁行分桶统计", 0)
ws0.append(["口径", "数值", "说明"])
ws0.append(["索引总行数（库）", 60346, "原版 51326 + lost-words 补丁 9020（seq 60059-69078）"])
ws0.append(["补丁行（0级带码行）", 9020, "data/knowledge/icd-index-lost-words-patch-1031-v5.sql，全语料逐字兜底"])
ws0.append(["明细行·原版索引桶", bucket_rows.get("原版索引", 0), "主导词+编码对不在补丁集"])
ws0.append(["明细行·v5补丁行桶", bucket_rows.get("v5补丁行", 0), "本质是字典名逐字走索引通道，不反映索引匹配能力"])
ws0.append(["名字总数", sum(v[0] for v in top1_bucket.values()), ""])
ws0.append(["Top1来源=原版索引 名字数", top1_bucket.get("原版索引", [0, 0])[0], "「索引链路真实精度」看这一桶"])
ws0.append(["Top1来源=原版索引 且Top1命中", top1_bucket.get("原版索引", [0, 0])[1], ""])
ws0.append(["Top1来源=v5补丁行 名字数", top1_bucket.get("v5补丁行", [0, 0])[0], ""])
ws0.append(["Top1来源=v5补丁行 且Top1命中", top1_bucket.get("v5补丁行", [0, 0])[1], "逐字相等命中，覆盖率口径下近似必然命中"])
ws0.append(["Top1来源=—（无原文rank1）", top1_bucket.get("—", [0, 0])[0], ""])

# ---- 其余页原样复制 ----
for title in ("无推荐归因", "字段说明"):
    s = wb[title]
    w = out.create_sheet(title)
    for r in s.iter_rows(values_only=True):
        w.append(list(r))

wb.close()
out.save(OUT)
print("saved:", OUT)
