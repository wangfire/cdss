# -*- coding: utf-8 -*-
"""把《CM3索引整理_最终版.xlsx》转成 CM3（手术操作）索引库导入用的 JSON 批次。

输出: data/knowledge/cm3-index-batches/batch-0001.json ...（每批 1000 条）
字段映射与 Excel 列一一对应，全部保留。
"""
import json
import sys
import io
from pathlib import Path

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
import openpyxl

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "docs/superpowers/konwledge/CM3索引整理_最终版.xlsx"
SHEET = "索引中英对照"
OUT_DIR = ROOT / "data/knowledge/cm3-index-batches"
BATCH_SIZE = 1000

EXPECTED_HEADERS = [
    "序号", "索引原始名称", "索引原始英文名称", "索引名称", "索引编码",
    "索引级别", "父节点", "完整索引名称", "主导词状态",
]


def text_or_none(value) -> str | None:
    """Excel 空单元格可能是 None 或空串，统一归一为 NULL。"""
    if value is None:
        return None
    text = str(value).strip()
    return text or None


def main() -> int:
    wb = openpyxl.load_workbook(str(SOURCE), read_only=True)
    ws = wb[SHEET]

    rows = ws.iter_rows(values_only=True)
    headers = list(next(rows))
    if headers != EXPECTED_HEADERS:
        print("列头不匹配:", headers)
        return 1

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    for old in OUT_DIR.glob("batch-*.json"):
        old.unlink()

    batch = []
    batch_no = 0
    total = 0
    seen_seq = set()

    def flush():
        nonlocal batch, batch_no
        if not batch:
            return
        batch_no += 1
        path = OUT_DIR / f"batch-{batch_no:04d}.json"
        path.write_text(json.dumps({"items": batch}, ensure_ascii=False), encoding="utf-8")
        batch = []

    for r in rows:
        seq = r[0]
        if seq is None:
            continue
        seq = int(seq)
        if seq in seen_seq:
            print("重复序号:", seq)
            return 1
        seen_seq.add(seq)
        batch.append({
            "seqNo": seq,
            "rawName": str(r[1]).strip(),
            "rawNameEn": text_or_none(r[2]),
            "indexName": str(r[3]).strip(),
            "indexCode": text_or_none(r[4]),
            "indexLevel": int(r[5]),
            "parentSeqNo": int(r[6]),
            "fullIndexName": str(r[7]).strip(),
            "leadWordStatus": int(r[8]),
        })
        total += 1
        if len(batch) >= BATCH_SIZE:
            flush()
    flush()
    wb.close()

    print(f"共 {total} 条，生成 {batch_no} 个批次 -> {OUT_DIR}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
