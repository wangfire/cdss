# -*- coding: utf-8 -*-
"""qwen2.5:3b 一致性校验任务抽样验证。
模拟流水线逐候选调用：诊断原文 + 候选 + 带编号证据原文 → 4 字段 JSON。
从 SQL Server 抽真实 FRONT_PAGE 输入与 ACTIVE 推荐证据，验证 cited_evidence 合法率与延迟。
"""
import io
import json
import sys
import time
import urllib.request

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

MODEL = sys.argv[1] if len(sys.argv) > 1 else "qwen2.5:3b-instruct"
SAMPLE_N = int(sys.argv[2]) if len(sys.argv) > 2 else 30

SCHEMA = json.dumps({
    "type": "object",
    "required": ["code", "supported", "cited_evidence", "negation_or_temporal_violation"],
    "properties": {
        "code": {"type": "string"},
        "supported": {"type": "boolean"},
        "cited_evidence": {"type": "array", "maxItems": 5, "items": {"type": "integer", "minimum": 1}},
        "negation_or_temporal_violation": {"type": "boolean"},
    },
    "additionalProperties": False,
}, ensure_ascii=False)

SYSTEM = "你是 ICD 编码推荐的一致性校验器：只判断给定证据原文能否支持给定候选编码，只输出 JSON。"


def call_ollama(prompt: str) -> tuple[str, float]:
    body = json.dumps({
        "model": MODEL,
        "stream": False,
        "format": "json",
        "options": {"num_predict": 160, "temperature": 0},
        "messages": [
            {"role": "system", "content": SYSTEM},
            {"role": "user", "content": prompt + "\n\n输出必须是严格符合以下 JSON Schema 的单个 JSON 对象，不要输出任何其他文本或代码块标记：\n" + SCHEMA},
        ],
    }).encode("utf-8")
    req = urllib.request.Request(
        "http://localhost:11434/api/chat", data=body,
        headers={"Content-Type": "application/json"})
    start = time.perf_counter()
    with urllib.request.urlopen(req, timeout=300) as resp:
        payload = json.loads(resp.read().decode("utf-8"))
    elapsed = time.perf_counter() - start
    return payload["message"]["content"], elapsed


def build_prompt(input_name: str, code: str, title: str, evidences: list[str], fact_line: str) -> str:
    lines = "\n".join(f"{i + 1}. {e[:120]}" for i, e in enumerate(evidences)) or "（无证据原文）"
    return (
        f"诊断原文：{input_name[:60]}\n"
        f"候选编码：{code} {title[:60]}\n"
        f"证据原文（引用编号从 1 开始）：\n{lines}\n"
        f"事实线索：{fact_line}\n"
        f"问题：上述证据原文能否支持把本条诊断编为候选编码？\n"
        f"- code：只填候选编码的编码值本身，不要带名称、说明或其他文字。\n"
        f"- supported：能支持填 true，不能支持或证据不足填 false。\n"
        f"- cited_evidence：支撑该判断的证据编号列表，只能使用上面列出的编号；无可用证据时填空数组 []。\n"
        f"- negation_or_temporal_violation：证据表明该诊断被否定、属于既往史或家族史时填 true。"
    )


def fetch_samples() -> list[dict]:
    import pyodbc
    conn_str = (
        "Driver={ODBC Driver 18 for SQL Server};"
        "Server=localhost,14333;Database=HospitalAi;"
        "UID=sa;PWD=ChangeMe_1234!;TrustServerCertificate=yes;Encrypt=no;"
    )
    query = """
    SELECT TOP (@n)
        i.original_text,
        r.code, r.title,
        e.original_text AS evidence_text,
        i.id AS input_id, r.id AS rec_id
    FROM coding_diagnosis_input i
    JOIN coding_recommendation r
        ON r.diagnosis_input_id = i.id AND r.lifecycle_status = 'ACTIVE' AND r.rank = 1
    JOIN clinical_evidence e
        ON e.hospital_id = r.hospital_id
       AND e.coding_task_id = r.coding_task_id
       AND e.status = 'ACTIVE'
       AND (e.original_text LIKE N'%' + r.title + N'%' OR r.title LIKE N'%' + e.original_text + N'%')
    WHERE i.source_type = 'FRONT_PAGE'
    ORDER BY i.original_text
    """
    conn = pyodbc.connect(conn_str, timeout=30)
    cursor = conn.cursor()
    cursor.execute(query.replace("@n", str(SAMPLE_N * 2)))
    rows = cursor.fetchall()
    cursor.close()
    conn.close()

    grouped: dict[tuple, list] = {}
    for row in rows:
        grouped.setdefault((str(row.input_id), str(row.rec_id)), []).append(row)

    samples = []
    for (input_id, rec_id), items in grouped.items():
        samples.append({
            "input_name": items[0].original_text,
            "code": items[0].code,
            "title": items[0].title,
            "evidences": [item.evidence_text for item in items][:3],
        })
        if len(samples) >= SAMPLE_N:
            break
    return samples


def main() -> None:
    samples = fetch_samples()
    print(f"model={MODEL} samples={len(samples)}")
    stats = {
        "calls": 0, "json_ok": 0, "schema_ok": 0,
        "echo_ok": 0, "cited_nonempty": 0, "cited_in_range": 0,
        "latencies": [],
    }
    failures = []
    for index, sample in enumerate(samples):
        evidences = sample["evidences"] or ["（无证据原文）"]
        fact_line = "否定=未知 确定性=Unknown 时间=Unknown"
        prompt = build_prompt(
            sample["input_name"], sample["code"], sample["title"], evidences, fact_line)
        try:
            content, elapsed = call_ollama(prompt)
        except Exception as exc:  # noqa: BLE001
            failures.append({"index": index, "error": str(exc)})
            continue

        stats["calls"] += 1
        stats["latencies"].append(elapsed)
        try:
            obj = json.loads(content)
            stats["json_ok"] += 1
        except json.JSONDecodeError:
            failures.append({"index": index, "content": content[:120]})
            continue

        required = {"code", "supported", "cited_evidence", "negation_or_temporal_violation"}
        cited = obj.get("cited_evidence")
        schema_ok = (
            required.issubset(obj)
            and isinstance(obj.get("supported"), bool)
            and isinstance(cited, list)
            and all(isinstance(x, int) and 1 <= x <= len(evidences) for x in cited)
            and isinstance(obj.get("negation_or_temporal_violation"), bool)
        )
        if schema_ok:
            stats["schema_ok"] += 1
        echo_ok = (
            isinstance(obj.get("code"), str)
            and obj["code"].replace(" ", "").replace("†", "+").upper()
            == sample["code"].replace(" ", "").replace("†", "+").upper()
        )
        if echo_ok:
            stats["echo_ok"] += 1
        if isinstance(cited, list) and len(cited) > 0:
            stats["cited_nonempty"] += 1
        if schema_ok and cited:
            stats["cited_in_range"] += 1
        print(f"[{index + 1}/{len(samples)}] {elapsed:5.2f}s code_ok={echo_ok} "
              f"cited={cited} supported={obj.get('supported')}")

    lat = sorted(stats["latencies"])
    print("\n===== SUMMARY =====")
    print(f"model={MODEL}")
    print(f"calls={stats['calls']} json_ok={stats['json_ok']} schema_ok={stats['schema_ok']}")
    print(f"echo_ok={stats['echo_ok']} cited_nonempty={stats['cited_nonempty']} "
          f"cited_in_range={stats['cited_in_range']}")
    if lat:
        print(f"latency p50={lat[len(lat)//2]:.2f}s p90={lat[int(len(lat)*0.9)]:.2f}s "
              f"max={lat[-1]:.2f}s mean={sum(lat)/len(lat):.2f}s")
    if failures:
        print(f"failures={len(failures)}")
        for item in failures[:3]:
            print(" ", json.dumps(item, ensure_ascii=False)[:200])


if __name__ == "__main__":
    main()
