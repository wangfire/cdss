# -*- coding: utf-8 -*-
"""Knowledge.vue i18n 收口（最后 10 行）：
   1. 示例数据（sampleSynonyms/sampleRules）提取到独立常量文件（领域数据保留中文，非 UI 文案）；
   2. Textarea placeholder 的内嵌 JSON 示例改为 t('knowledge.jsonPlaceholder') 通用提示；
   3. zh/en locale 补 jsonPlaceholder key。
"""
import io
import os
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

# ---- 1. 提取示例数据到独立常量文件 ----
const_path = r"web-ui\apps\admin\src\constants\knowledge-samples.ts"
os.makedirs(os.path.dirname(const_path), exist_ok=True)
const_content = """/**
 * 知识库导入示例数据（领域数据，非 UI 文案——中文为 ICD 术语本身，不参与 i18n）
 */
export interface TermSynonymImportItem {
  term: string
  normalizedTerm: string
  entityType: string
  codeSystemCode: string
  code: string
}

export interface CodingRuleImportItem {
  ruleCode: string
  codeSystem: string
  codePattern: string
  ruleType: string
  severity: string
  message: string
  isEnabled: boolean
}

export const sampleSynonyms: TermSynonymImportItem[] = [
  { term: '感冒', normalizedTerm: '急性鼻咽炎', entityType: 'DISEASE', codeSystemCode: 'ICD-10', code: 'J00' },
  { term: '高血压', normalizedTerm: '特发性高血压', entityType: 'DISEASE', codeSystemCode: 'ICD-10', code: 'I10' },
  { term: '糖尿病', normalizedTerm: '非胰岛素依赖型糖尿病', entityType: 'DISEASE', codeSystemCode: 'ICD-10', code: 'E11' },
  { term: '阑尾炎手术', normalizedTerm: '阑尾切除术', entityType: 'PROCEDURE', codeSystemCode: 'ICD-9-CM-3', code: '47.0' },
  { term: '搭桥手术', normalizedTerm: '冠状动脉旁路移植术', entityType: 'PROCEDURE', codeSystemCode: 'ICD-9-CM-3', code: '36.1' },
]

export const sampleRules: CodingRuleImportItem[] = [
  { ruleCode: 'SZ-001', codeSystem: 'ICD-10', codePattern: 'J00-J06', ruleType: 'VALIDATION', severity: 'WARNING', message: '上呼吸道感染编码需确认病程', isEnabled: true },
  { ruleCode: 'SZ-002', codeSystem: 'ICD-10', codePattern: 'I10-I15', ruleType: 'VALIDATION', severity: 'ERROR', message: '高血压编码需区分原发性和继发性', isEnabled: true },
  { ruleCode: 'SZ-003', codeSystem: 'ICD-10', codePattern: 'E10-E14', ruleType: 'VALIDATION', severity: 'WARNING', message: '糖尿病编码需确认分型', isEnabled: true },
]
"""
open(const_path, "w", encoding="utf-8").write(const_content)
print("created:", const_path)

# ---- 2. Knowledge.vue 引用常量文件 + placeholder 改 t() ----
p = r"web-ui\apps\admin\src\views\Knowledge.vue"
c = open(p, encoding="utf-8").read()

# 删除内嵌的 sampleSynonyms/sampleRules 定义
import re
old_syn = re.search(
    r"const sampleSynonyms: TermSynonymImportItem\[\] = \[[\s\S]*?\]\n\n", c
)
old_rules = re.search(
    r"const sampleRules: CodingRuleImportItem\[\] = \[[\s\S]*?\]\n\n", c
)
if old_syn:
    c = c.replace(old_syn.group(0), "")
    print("removed inline sampleSynonyms")
if old_rules:
    c = c.replace(old_rules.group(0), "")
    print("removed inline sampleRules")

# 加 import
if "knowledge-samples" not in c:
    m = re.search(r"(import \{[^}]*\} from '@/api'\n)", c)
    if m:
        c = c.replace(
            m.group(1),
            m.group(1) + "import { sampleSynonyms, sampleRules } from '@/constants/knowledge-samples'\n",
            1,
        )
    else:
        # 找第一个 import 行后插入
        c = c.replace(
            "<script setup lang=\"ts\">\n",
            "<script setup lang=\"ts\">\nimport { sampleSynonyms, sampleRules } from '@/constants/knowledge-samples'\n",
            1,
        )
    print("import added")

# placeholder 改 t()
c = c.replace(
    ''':placeholder="'[\\n  { &quot;term&quot;: &quot;感冒&quot;, &quot;normalizedTerm&quot;: &quot;急性鼻咽炎&quot;, &quot;entityType&quot;: &quot;DISEASE&quot;, &quot;codeSystemCode&quot;: &quot;ICD-10&quot;, &quot;code&quot;: &quot;J00&quot; }\\n]'"''',
    ''':placeholder="t('knowledge.jsonPlaceholder')"''',
)
c = c.replace(
    ''':placeholder="'[\\n  { &quot;ruleCode&quot;: &quot;SZ-001&quot;, &quot;codeSystem&quot;: &quot;ICD-10&quot;, &quot;codePattern&quot;: &quot;J00-J06&quot;, &quot;ruleType&quot;: &quot;VALIDATION&quot;, &quot;severity&quot;: &quot;WARNING&quot;, &quot;message&quot;: &quot;上呼吸道感染编码需确认病程&quot;, &quot;isEnabled&quot;: true }\\n]'"''',
    ''':placeholder="t('knowledge.jsonPlaceholder')"''',
)
print("placeholders replaced")

open(p, "w", encoding="utf-8").write(c)

# ---- 3. locale 补 jsonPlaceholder ----
for lang, val in (
    ("zh-CN", "JSON 格式的同义词/规则数据（可点击「加载示例数据」快速填充）"),
    ("en-US", "Synonym/rule data in JSON format (click \"Load sample data\" to fill)"),
):
    lp = rf"web-ui\apps\admin\src\i18n\locales\{lang}\knowledge.ts"
    lc = open(lp, encoding="utf-8").read()
    if "jsonPlaceholder" not in lc:
        lines = lc.rstrip("\n").split("\n")
        close_idx = next(i for i in range(len(lines) - 1, -1, -1) if lines[i].strip() == "}")
        lc_keys = {"jsonPlaceholder": val}
        additions = "\n".join(f"  {k}: '{v}'," for k, v in lc_keys.items())
        lines.insert(close_idx, additions)
        open(lp, "w", encoding="utf-8").write("\n".join(lines))
        print(lp, "jsonPlaceholder added")
