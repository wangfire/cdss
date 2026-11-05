# -*- coding: utf-8 -*-
"""组装 编码录入·全量细码主导词修饰词对照 Excel 工作簿（格式对齐 2026-11-02-补丁行分桶版）。"""
import sys
import openpyxl
from openpyxl.styles import Font, PatternFill, Alignment
from openpyxl.utils import get_column_letter

tsv_path = sys.argv[1]
xlsx_path = sys.argv[2]

rows = []
with open(tsv_path, encoding="utf-8") as f:
    header = f.readline().rstrip("\n").split("\t")
    for line in f:
        parts = line.rstrip("\n").split("\t")
        parts += [""] * (len(header) - len(parts))
        rows.append(dict(zip(header, parts)))

col = {name: i for i, name in enumerate(header)}
total = len(rows)
matched = sum(1 for r in rows if r["可匹配"] == "✓")
no_lead = sum(1 for r in rows if r["不可匹配原因"] == "无主导词召回")
no_chain = sum(1 for r in rows if r["不可匹配原因"] == "链路无产出")
fb = sum(1 for r in rows if r["不可匹配原因"] == "链路展开为回退码")
diff = sum(1 for r in rows if r["不可匹配原因"] == "链路码与对照码不同族")

wb = openpyxl.Workbook()

# ---- Sheet 明细 ----
ws = wb.active
ws.title = "全量细码逐条明细"
head_fill = PatternFill("solid", fgColor="1F4E79")
for c, name in enumerate(header, 1):
    cell = ws.cell(row=1, column=c, value=name)
    cell.font = Font(bold=True, color="FFFFFF")
    cell.fill = head_fill
    cell.alignment = Alignment(vertical="center")
r = 2
for row in rows:
    for c, name in enumerate(header, 1):
        ws.cell(row=r, column=c, value=row.get(name, ""))
    fill = None
    if row["可匹配"] == "✓":
        fill = PatternFill("solid", fgColor="E2EFDA") if row["匹配方式"] == "索引链路精确" else PatternFill("solid", fgColor="FFF2CC")
    else:
        fill = PatternFill("solid", fgColor="FCE4EC")
    ws.cell(row=r, column=col["可匹配"] + 1).fill = fill
    r += 1
widths = [6, 14, 30, 8, 14, 8, 32, 20, 44, 14, 26, 14, 24, 10, 34, 10]
for i, w in enumerate(widths, 1):
    ws.column_dimensions[get_column_letter(i)].width = w
ws.freeze_panes = "D2"
ws.auto_filter.ref = f"A1:{get_column_letter(len(header))}{max(2, r - 1)}"

# ---- Sheet 分桶统计 ----
ws2 = wb.create_sheet("分桶统计")
ws2.append(["指标", "数值", "占比"])
for row in [
    ("细码总数", total, "100%"),
    ("可匹配（Top1 为对照码或同族非回退码）", matched, f"{matched * 100.0 / max(1, total):.1f}%"),
    ("不可匹配合计", total - matched, f"{(total - matched) * 100.0 / max(1, total):.1f}%"),
    ("  ├ 无主导词召回", no_lead, f"{no_lead * 100.0 / max(1, total):.1f}%"),
    ("  ├ 链路无产出", no_chain, f"{no_chain * 100.0 / max(1, total):.1f}%"),
    ("  ├ 链路展开为回退码", fb, f"{fb * 100.0 / max(1, total):.1f}%"),
    ("  └ 链路码与对照码不同族", diff, f"{diff * 100.0 / max(1, total):.1f}%"),
]:
    ws2.append(list(row))
for c in "ABC":
    ws2.column_dimensions[c].width = 40 if c == "A" else 14
for cell in ws2["A"]:
    if cell.value and str(cell.value).startswith(("可匹配", "不可")):
        cell.font = Font(bold=True)

# ---- Sheet 字段说明 ----
ws3 = wb.create_sheet("字段说明")
notes = [
    ("字段", "含义"),
    ("编码/诊断名称", "medical_code 筛选：ICD-10、启用、编码后两位≠00、不含 +/*/ 组合码（即全部细码），DISTINCT 去重"),
    ("可匹配", "✓=Top1 为非回退码，且码值=对照码本身或与其三位类目同族；✗=不可匹配（看不可匹配原因）"),
    ("匹配方式", "索引链路精确（Top1 编码=对照码）/ 索引链路同族（同三位类目）/ 字典标题直达 / 索引回退·反查 / 无推荐"),
    ("全量命中主导词数", "编码录入引擎的主导词全量匹配命中数（拆分口径见 docs/2026-11-03 方案：全量匹配+噪声防线）"),
    ("主导词(显示词面)", "同义族逗号行显示头段（梗死，梗塞→梗死）；转示行显示 键→目标（阿米巴→阿米巴病）"),
    ("命中的别名键", "各主导词实际命中的归一化别名"),
    ("修饰词链路(索引树原样)", "各主导词下按索引树走出的修饰词路径（→ 分隔，严格索引树层级，不折叠不加步骤）"),
    ("链路索引编码", "各链路终点行的索引编码（无=该链无码）"),
    ("字典展开码", "索引编码在本院 ICD-10 字典展开出的细码，(fb)=回退码"),
    ("Top1 编码/标题/是否回退/Top1 链路", "本次解析 Top1 候选及其证据链（标题直达通道已关，全部由索引链路自产）"),
    ("不可匹配原因", "无主导词召回 / 链路无产出 / 链路展开为回退码 / 链路码与对照码不同族"),
    ("本轮口径", "NameCodingLookupEngine（编码录入生产引擎）：标题直达关、同族反查开；tc 排序=展开器分×回退惩罚，平手按整标题贴合；主链路核心词核对+无修饰词/回退码补钻（核心词入文严格准入、因果行分过道/死路）+枚举字面占位核对；展示链路严格索引树"),
    ("用途", "全量筛查『哪些细码能经索引链路匹配、哪些不能』：✗ 行即索引缺口清单，按不可匹配原因分桶定位（缺主导词/缺修饰词/缺字典细码/码值漂移）"),
]
for row in notes:
    ws3.append(list(row))
ws3.column_dimensions["A"].width = 26
ws3.column_dimensions["B"].width = 120
for cell in ws3["A"]:
    cell.font = Font(bold=True)
for row in ws3.iter_rows(min_row=2):
    row[1].alignment = Alignment(wrap_text=True, vertical="top")

wb.save(xlsx_path)
print(f"written {xlsx_path}: rows={total} matched={matched}")
