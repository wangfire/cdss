#!/usr/bin/env python3
"""把知识库 Excel 整理为 Phase 2 可导入的 JSON。

脚本只读取 docs/superpowers/konwledge 下的原始 Excel，输出到 data/knowledge。
所有字段映射都集中在本文件中，便于后续替换新版本资料后重复构建。
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import re
import unicodedata
from collections import defaultdict
from dataclasses import dataclass
from datetime import date, datetime
from pathlib import Path
from typing import Any, Iterable

import openpyxl


ROOT = Path(__file__).resolve().parents[2]
SOURCE_DIR = ROOT / "docs" / "superpowers" / "konwledge"
OUTPUT_DIR = ROOT / "data" / "knowledge"
REPORT_DIR = OUTPUT_DIR / "reports"

DISEASE_FILE = SOURCE_DIR / "疾病三库对照表.xlsx"
PROCEDURE_FILE = SOURCE_DIR / "手术三库对照表.xlsx"
INDEX_FILE = SOURCE_DIR / "ICD10索引关键词编码提取结果(20260909).xlsx"
RULE_FILE = SOURCE_DIR / "深圳医保规则.xlsx"


@dataclass(frozen=True)
class SourceValue:
    source: str
    code: str
    title: str
    aliases: tuple[str, ...]
    status: str
    row_number: int


def clean_text(value: Any) -> str:
    """统一全角字符和空白，保留编码中的 `†`、`*` 语义标记。"""
    if value is None:
        return ""
    text = unicodedata.normalize("NFKC", str(value))
    return re.sub(r"\s+", " ", text).strip()


def clean_code(value: Any) -> str:
    """清理字典编码格式噪声，但保留组合码和形态学标记。"""
    text = clean_text(value).replace(" ", "")
    return text.upper() if text else ""


def split_index_codes(value: Any) -> list[str]:
    """解析 ICD 索引编码。

    `†` 表示与后续编码组成一个组合编码，需转换为 `+`；
    没有 `†` 的空白分隔编码才拆成多条关系。`*` 是形态学标记，必须保留。
    """
    text = clean_text(value)
    if not text:
        return []

    text = text.replace("†", "+")
    text = re.sub(r"\s*\+\s*", "+", text)
    if "+" in text:
        return [clean_code(text)]

    return [
        clean_code(part)
        for part in re.split(r"[\s,，;；、]+", text)
        if clean_code(part)
    ]


def split_aliases(value: Any) -> list[str]:
    text = clean_text(value)
    if not text:
        return []
    parts = re.split(r"[；;、|/]+", text)
    return [part.strip(" []（）()") for part in parts if part.strip(" []（）()")]


def unique(values: Iterable[str]) -> list[str]:
    result: list[str] = []
    seen: set[str] = set()
    for value in values:
        value = clean_text(value)
        if value and value not in seen:
            seen.add(value)
            result.append(value)
    return result


def json_default(value: Any) -> str:
    if isinstance(value, (datetime, date)):
        return value.isoformat()
    return str(value)


def read_rows(path: Path, sheet_name: str) -> list[tuple[int, tuple[Any, ...]]]:
    workbook = openpyxl.load_workbook(path, read_only=True, data_only=True)
    worksheet = workbook[sheet_name]
    return [
        (row_number, tuple(row))
        for row_number, row in enumerate(
            worksheet.iter_rows(min_row=2, values_only=True), start=2
        )
    ]


def parse_three_library(
    path: Path,
    sheet_name: str,
    groups: list[tuple[str, int, int, int, int]],
) -> tuple[dict[str, list[SourceValue]], dict[str, int]]:
    """读取三库对照表并按编码聚合三个来源。"""
    by_code: dict[str, list[SourceValue]] = defaultdict(list)
    stats = {"source_rows": 0, "valid_rows": 0, "invalid_rows": 0}

    for row_number, row in read_rows(path, sheet_name):
        for source, code_index, title_index, status_index, alias_index in groups:
            stats["source_rows"] += 1
            code = clean_code(row[code_index] if code_index < len(row) else None)
            title = clean_text(row[title_index] if title_index < len(row) else None)
            if not code or not title:
                stats["invalid_rows"] += 1
                continue
            aliases = tuple(
                unique(
                    split_aliases(row[alias_index] if alias_index < len(row) else None)
                )
            )
            status = clean_text(row[status_index] if status_index < len(row) else None)
            by_code[code].append(
                SourceValue(source, code, title, aliases, status, row_number)
            )
            stats["valid_rows"] += 1
    return by_code, stats


def choose_display_value(values: list[SourceValue]) -> SourceValue:
    """优先展示医保名称，其次国临、国标，保证展示名稳定。"""
    priority = {"医保": 0, "国临": 1, "国标": 2}
    return sorted(values, key=lambda item: (priority.get(item.source, 99), item.row_number))[0]


def build_codes(
    by_code: dict[str, list[SourceValue]],
    code_type: str,
) -> tuple[list[dict[str, Any]], dict[str, Any]]:
    codes: list[dict[str, Any]] = []
    disabled: list[dict[str, Any]] = []
    duplicate_source_rows = 0

    for code, values in sorted(by_code.items()):
        active_values = [item for item in values if item.status == "0"]
        enabled = bool(active_values)
        display = choose_display_value(values)
        titles = unique(item.title for item in values)
        aliases = unique(
            alias
            for item in values
            for alias in item.aliases
        )
        search_text = " ".join(unique([display.title, *titles, *aliases]))[:1000]
        item = {
            "code": code,
            "title": display.title,
            "codeType": code_type,
            "searchText": search_text,
            "isEnabled": enabled,
        }
        codes.append(item)
        if not enabled:
            disabled.append(
                {
                    "code": code,
                    "title": display.title,
                    "statuses": sorted({item.status for item in values}),
                    "sources": sorted({item.source for item in values}),
                }
            )
        duplicate_source_rows += max(0, len(values) - 1)

    return codes, {
        "code_count": len(codes),
        "enabled_count": sum(1 for item in codes if item["isEnabled"]),
        "disabled_count": len(disabled),
        "duplicate_source_rows": duplicate_source_rows,
        "disabled_codes": disabled,
    }


def extract_code_tokens(value: Any) -> list[str]:
    text = clean_code(value)
    if not text:
        return []
    # 索引表可能含有多个编码、†、* 和形态学编码，只提取可用于 ICD-10 反查的字母数字码。
    return re.findall(r"\b[A-Z][0-9]{2}(?:\.[0-9A-Z]+)?\b", text)


def code_keys(value: str) -> set[str]:
    """生成 ICD 短码、三位小数码和扩展码的等价检索键。"""
    value = clean_code(value)
    if not value:
        return set()

    keys = {value}
    for part in re.split(r"[+ ]+", value):
        if not re.match(r"^[A-Z][0-9]{2}(?:\.[0-9A-Z]+)?$", part):
            continue
        keys.add(part)
        if "." in part:
            prefix, suffix = part.split(".", 1)
            trimmed_suffix = suffix.rstrip("0") or "0"
            keys.add(f"{prefix}.{trimmed_suffix}")
            # 三库扩展码如 A06.200X001，索引常只保留 A06.2。
            extension_index = trimmed_suffix.upper().find("X")
            if extension_index > 0:
                base_suffix = trimmed_suffix[:extension_index].rstrip("0") or "0"
                keys.add(f"{prefix}.{base_suffix}")
    return keys


def build_index_synonyms(
    headers: list[str],
    rows: Iterable[tuple[Any, ...]],
) -> list[dict[str, Any]]:
    """将 ICD-10 索引表直接整理为“术语 -> 编码”关系。"""
    positions = {header: index for index, header in enumerate(headers)}
    required_headers = [
        "编码",
        "名称(主词)",
        "病(拆分)",
        "综合征(拆分)",
        "[]内容-1",
        "[]内容-2",
        "[]内容-3",
        "NEC状态",
    ]
    missing = [header for header in required_headers if header not in positions]
    if missing:
        raise ValueError(f"索引表缺少列：{', '.join(missing)}")

    # 数据库唯一键不包含归一化术语，因此同一关键词和编码只能保留一条关系。
    synonyms_by_key: dict[tuple[str, str, str, str], dict[str, Any]] = {}
    for row in rows:
        status = clean_text(row[positions["NEC状态"]] if positions["NEC状态"] < len(row) else None)
        if status != "0":
            continue

        codes = split_index_codes(row[positions["编码"]] if positions["编码"] < len(row) else None)
        main_term = clean_text(
            row[positions["名称(主词)"]] if positions["名称(主词)"] < len(row) else None
        )
        terms = unique(
            clean_text(row[positions[header]] if positions[header] < len(row) else None)
            for header in required_headers[1:-1]
        )
        for code in codes:
            for term in terms:
                key = (term, "ICD-10", "DIAGNOSIS", code)
                synonyms_by_key[key] = {
                    "term": term,
                    "normalizedTerm": main_term or term,
                    "codeSystemCode": "ICD-10",
                    "code": code,
                    "entityType": "DIAGNOSIS",
                }

    return sorted(
        synonyms_by_key.values(),
        key=lambda item: (item["term"], item["code"]),
    )


def build_synonyms(
    disease_codes: list[dict[str, Any]],
) -> tuple[list[dict[str, Any]], list[dict[str, Any]], dict[str, int]]:
    """兼容旧调用入口，实际按索引表直接构建编码映射。"""
    del disease_codes
    workbook = openpyxl.load_workbook(INDEX_FILE, read_only=True, data_only=True)
    worksheet = workbook["提取结果"]
    header_row = next(worksheet.iter_rows(min_row=1, max_row=1, values_only=True))
    headers = [clean_text(value) for value in header_row]
    rows = worksheet.iter_rows(min_row=2, values_only=True)
    synonyms = build_index_synonyms(headers, rows)
    return (
        synonyms,
        [],
        {
            "source_rows": worksheet.max_row - 1,
            "matched_rows": worksheet.max_row - 1,
            "unmatched_rows": 0,
            "synonym_count": len(synonyms),
        },
    )


def stable_rule_code(row_number: int, row: tuple[Any, ...]) -> str:
    material = "|".join(clean_text(value) for value in row[:22])
    digest = hashlib.sha1(material.encode("utf-8")).hexdigest()[:10]
    return f"SZ医保-{row_number:04d}-{digest}"


def infer_rule_system(rule_type: str, condition1: str, condition2: str) -> str:
    text = f"{rule_type} {condition1} {condition2}"
    has_diagnosis = "疾病" in text or "诊断" in text
    has_procedure = "手术" in text or "操作" in text
    if has_diagnosis and has_procedure:
        return "ALL"
    if has_procedure:
        return "ICD-9-CM-3"
    return "ICD-10"


def infer_rule_type(category: str, condition1: str, condition2: str) -> str:
    text = f"{category} {condition1} {condition2}"
    if "年龄" in text:
        return "AGE_CONFLICT"
    if "性别" in text:
        return "GENDER_CONFLICT"
    if "手术" in text or "操作" in text:
        return "PROCEDURE_SELECTION"
    if "主要诊断" in text or "疾病" in text or "诊断" in text:
        return "DIAGNOSIS_SELECTION"
    return "MEDICAL_INSURANCE_VALIDATION"


def build_rules() -> tuple[list[dict[str, Any]], list[dict[str, Any]], dict[str, int]]:
    rows = read_rows(RULE_FILE, "Sheet1")
    rules: list[dict[str, Any]] = []
    unsupported: list[dict[str, Any]] = []

    for row_number, row in rows:
        category = clean_text(row[0] if len(row) > 0 else None)
        if not category or category.startswith("F_"):
            continue
        condition1_type = clean_text(row[5] if len(row) > 5 else None)
        condition1_code = clean_text(row[6] if len(row) > 6 else None)
        condition1_exclude = clean_text(row[7] if len(row) > 7 else None)
        condition1_name = clean_text(row[8] if len(row) > 8 else None)
        condition2_type = clean_text(row[9] if len(row) > 9 else None)
        condition2_code = clean_text(row[10] if len(row) > 10 else None)
        condition2_exclude = clean_text(row[11] if len(row) > 11 else None)
        condition2_name = clean_text(row[12] if len(row) > 12 else None)
        check_description = clean_text(row[13] if len(row) > 13 else None)
        result = clean_text(row[15] if len(row) > 15 else None)
        result_name = clean_text(row[16] if len(row) > 16 else None)
        error_code = clean_text(row[17] if len(row) > 17 else None)
        sure_result = clean_text(row[18] if len(row) > 18 else None)
        recommend_code = clean_text(row[19] if len(row) > 19 else None)
        recommend_name = clean_text(row[20] if len(row) > 20 else None)

        if not condition1_type and not condition1_code and not result:
            continue

        code_pattern = condition1_code or "*"
        if condition2_code:
            code_pattern = f"{code_pattern}|{condition2_code}"
        code_pattern = code_pattern[:128]
        message_parts = [
            f"规则分类：{category}",
            f"条件1：{condition1_type} {condition1_code} {condition1_name}".strip(),
        ]
        if condition1_exclude:
            message_parts.append(f"条件1排除：{condition1_exclude}")
        if condition2_type or condition2_code or condition2_name:
            message_parts.append(
                f"条件2：{condition2_type} {condition2_code} {condition2_name}".strip()
            )
        if condition2_exclude:
            message_parts.append(f"条件2排除：{condition2_exclude}")
        if check_description:
            message_parts.append(f"判断：{check_description}")
        if result:
            message_parts.append(f"校验结果：{result}")
        if result_name:
            message_parts.append(f"结果名称：{result_name}")
        if recommend_code or recommend_name:
            message_parts.append(f"推荐：{recommend_code} {recommend_name}".strip())
        if error_code:
            message_parts.append(f"错误提示：{error_code}")
        if sure_result:
            message_parts.append(f"明确结果：{sure_result}")

        category_type = infer_rule_type(category, condition1_type, condition2_type)
        rule = {
            "ruleCode": stable_rule_code(row_number, row),
            "codeSystem": infer_rule_system(category, condition1_type, condition2_type),
            "codePattern": code_pattern,
            "ruleType": category_type,
            "severity": "ERROR" if "错误" in category or "冲突" in category else "WARNING",
            "message": "；".join(message_parts)[:500],
            "isEnabled": True,
        }
        rules.append(rule)

        if (
            condition1_exclude
            or condition2_exclude
            or "|" in code_pattern
            or any(symbol in (condition1_code + condition2_code) for symbol in ("&", "，", "-", "至"))
            or "年龄" in check_description
            or "性别" in check_description
        ):
            unsupported.append(
                {
                    "row": row_number,
                    "ruleCode": rule["ruleCode"],
                    "category": category,
                    "reason": "当前基础规则模型可保存该规则描述，但尚未实现多条件、排除码、年龄/性别等执行器。",
                    "condition1": f"{condition1_type} {condition1_code} {condition1_name}".strip(),
                    "condition2": f"{condition2_type} {condition2_code} {condition2_name}".strip(),
                }
            )

    return rules, unsupported, {
        "source_rows": len(rows),
        "rule_count": len(rules),
        "unsupported_count": len(unsupported),
    }


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(value, ensure_ascii=False, indent=2, default=json_default) + "\n",
        encoding="utf-8",
    )


def write_csv(path: Path, rows: list[dict[str, Any]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    if not rows:
        path.write_text("", encoding="utf-8")
        return
    with path.open("w", newline="", encoding="utf-8-sig") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(rows[0].keys()))
        writer.writeheader()
        writer.writerows(rows)


def build() -> dict[str, Any]:
    disease_sources, disease_source_stats = parse_three_library(
        DISEASE_FILE,
        "对照表",
        [("国临", 2, 3, 1, 4), ("医保", 7, 8, 6, 9), ("国标", 12, 13, 11, 14)],
    )
    procedure_sources, procedure_source_stats = parse_three_library(
        PROCEDURE_FILE,
        "Sheet1",
        [("国临", 1, 2, 3, 6), ("医保", 10, 11, 14, 12), ("国标", 15, 16, 20, 18)],
    )
    disease_codes, disease_stats = build_codes(disease_sources, "诊断")
    procedure_codes, procedure_stats = build_codes(procedure_sources, "手术")
    synonyms, unmatched_keywords, synonym_stats = build_synonyms(disease_codes)
    rules, unsupported_rules, rule_stats = build_rules()

    write_json(
        OUTPUT_DIR / "icd10-2026.json",
        {"codeSystem": "ICD-10", "version": "2026", "codes": disease_codes},
    )
    write_json(
        OUTPUT_DIR / "icd9cm3-2026.json",
        {"codeSystem": "ICD-9-CM-3", "version": "2026", "codes": procedure_codes},
    )
    write_json(
        OUTPUT_DIR / "coding-rules-2026.json",
        {"synonyms": synonyms, "rules": rules},
    )
    write_csv(REPORT_DIR / "unmatched-keywords.csv", unmatched_keywords)
    write_csv(REPORT_DIR / "unsupported-rules.csv", unsupported_rules)

    manifest = {
        "generatedAt": datetime.now().astimezone().isoformat(),
        "sourceDirectory": str(SOURCE_DIR),
        "files": [
            {"name": path.name, "size": path.stat().st_size}
            for path in (DISEASE_FILE, PROCEDURE_FILE, INDEX_FILE, RULE_FILE)
        ],
        "outputs": {
            "icd10": len(disease_codes),
            "icd9cm3": len(procedure_codes),
            "synonyms": len(synonyms),
            "rules": len(rules),
        },
    }
    write_json(OUTPUT_DIR / "source-manifest.json", manifest)

    report = {
        "疾病三库对照表": {**disease_source_stats, **disease_stats},
        "手术三库对照表": {**procedure_source_stats, **procedure_stats},
        "ICD10索引关键词": synonym_stats,
        "深圳医保规则": rule_stats,
    }
    report_lines = [
        "# 底层知识库构建报告",
        "",
        f"生成时间：{manifest['generatedAt']}",
        "",
        "## 产物",
        "",
        f"- ICD-10 编码：{len(disease_codes)} 条，启用 {disease_stats['enabled_count']} 条，停用 {disease_stats['disabled_count']} 条。",
        f"- ICD-9-CM-3 编码：{len(procedure_codes)} 条，启用 {procedure_stats['enabled_count']} 条，停用 {procedure_stats['disabled_count']} 条。",
        f"- ICD-10 索引同义词：{len(synonyms)} 条。",
        f"- 深圳医保规则：{len(rules)} 条。",
        "",
        "## 源数据处理",
        "",
        f"- 疾病三库源行：{disease_source_stats['source_rows']}，有效 {disease_source_stats['valid_rows']}，无效 {disease_source_stats['invalid_rows']}，合并重复源行 {disease_stats['duplicate_source_rows']}。",
        f"- 手术三库源行：{procedure_source_stats['source_rows']}，有效 {procedure_source_stats['valid_rows']}，无效 {procedure_source_stats['invalid_rows']}，合并重复源行 {procedure_stats['duplicate_source_rows']}。",
        f"- ICD-10 索引行：{synonym_stats['source_rows']}，编码/名称匹配 {synonym_stats['matched_rows']}，未匹配 {synonym_stats['unmatched_rows']}。",
        f"- 规则源行：{rule_stats['source_rows']}，导入 {rule_stats['rule_count']}，需人工确认执行能力 {rule_stats['unsupported_count']}。",
        "",
        "## 重要说明",
        "",
        "- 三库编码按编码去重，展示名优先采用医保名称，国临/国标名称和别名合并到 searchText。",
        "- 只有至少一个来源状态为 0 的编码默认启用；没有状态 0 的编码保留但停用，便于追溯。",
        "- 深圳医保规则当前按描述型规则导入。多条件、排除码、年龄/性别约束已保留在 message，但尚未由 Phase 2 基线执行器自动判定。",
        "- 原始 Excel 未被修改。未匹配关键词和暂不支持自动执行的规则分别见 reports 目录。",
        "",
        "## 文件",
        "",
        "- `icd10-2026.json`",
        "- `icd9cm3-2026.json`",
        "- `coding-rules-2026.json`",
        "- `reports/unmatched-keywords.csv`",
        "- `reports/unsupported-rules.csv`",
        "- `source-manifest.json`",
    ]
    (REPORT_DIR / "knowledge-build-report.md").write_text(
        "\n".join(report_lines) + "\n", encoding="utf-8"
    )
    return report


def main() -> None:
    parser = argparse.ArgumentParser(description="构建 Phase 2 底层编码知识库")
    parser.add_argument("--self-test", action="store_true", help="执行最小映射自检")
    args = parser.parse_args()

    if args.self_test:
        assert split_index_codes(" E74.0† I43.1* ") == ["E74.0+I43.1*"]
        assert split_aliases("甲；乙/丙") == ["甲", "乙", "丙"]
        assert "Q91.7" in code_keys("Q91.700")
        assert "A06.2" in code_keys("A06.200X001")
        assert infer_rule_system("主要疾病选择错误", "主要疾病编码", "") == "ICD-10"
        assert infer_rule_system("主要手术选择错误", "主要手术编码", "") == "ICD-9-CM-3"
        print("自检通过")
        return

    report = build()
    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
