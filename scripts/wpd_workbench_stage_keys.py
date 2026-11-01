# -*- coding: utf-8 -*-
"""i18n：workbench.ts 补管道阶段/降级 key（zh/en）。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

zh_keys = {
    "stageQualityGate": "数据质量门",
    "stageDocumentVersion": "文书版本",
    "stageChunk": "文本分段",
    "stageFact": "事实抽取",
    "stageEvidence": "证据生成",
    "stageExactRetrieval": "精确检索",
    "stageBm25Retrieval": "词法检索(BM25)",
    "stageRule": "规则引擎",
    "stageScore": "评分",
    "stagePolicy": "策略判定",
    "stagePersist": "结果落库",
    "degradedNoBm25": "检索引擎不可用",
    "degradedNoVector": "向量检索未接入",
    "degradedNoModel": "大模型未接入",
    "degradedLlmInvalid": "模型输出未通过校验",
}
en_keys = {
    "stageQualityGate": "Data quality gate",
    "stageDocumentVersion": "Document version",
    "stageChunk": "Text chunking",
    "stageFact": "Fact extraction",
    "stageEvidence": "Evidence generation",
    "stageExactRetrieval": "Exact retrieval",
    "stageBm25Retrieval": "Lexical retrieval (BM25)",
    "stageRule": "Rule engine",
    "stageScore": "Scoring",
    "stagePolicy": "Policy decision",
    "stagePersist": "Result persistence",
    "degradedNoBm25": "Search engine unavailable",
    "degradedNoVector": "Vector search not connected",
    "degradedNoModel": "LLM not connected",
    "degradedLlmInvalid": "Model output failed validation",
}

files = [
    (r"web-ui\apps\admin\src\i18n\locales\zh-CN\workbench.ts", zh_keys),
    (r"web-ui\apps\admin\src\i18n\locales\en-US\workbench.ts", en_keys),
]
for path, keys in files:
    c = open(path, encoding="utf-8").read()
    if "stageQualityGate" in c:
        print(path, "already present")
        continue
    lines = c.rstrip("\n").split("\n")
    close_idx = next(i for i in range(len(lines) - 1, -1, -1) if lines[i].strip() == "}")
    additions = "\n".join(f"  {k}: '{v}'," for k, v in keys.items())
    lines.insert(close_idx, additions)
    open(path, "w", encoding="utf-8").write("\n".join(lines))
    print(path, f"keys added ({len(keys)})")
