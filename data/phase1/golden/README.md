# 离线评测目录（Golden Dataset）

本目录是 V2.2 离线评测的输入，**不是**代码，不参与编译。

```
data/phase1/golden/
  golden-dataset.json   # 用例集（当前 phase1-golden-v1，12 条）
  reports/              # evaluate 命令输出（每次运行按时间戳追加）
```

## 用例字段

| 字段 | 说明 |
| --- | --- |
| `caseId` | 用例唯一标识，报告与 Trace 都用它闭环 |
| `category` | 场景分类，必须覆盖方案要求的 12 类 |
| `documentText` | 病历正文，评测时写入临时文书 |
| `diagnosisText` | 医生 / 编码员原始诊断输入 |
| `expectedFacts` / `expectedNegation` / `expectedTemporality` / `expectedNormalization` | Fact 抽取期望 |
| `expectedEvidence` | 必须出现（或刻意不出现）的证据锚点 |
| `expectedCodes` | 期望编码集合；为空表示“不应出推荐” |
| `expectedRisk` | 期望风险等级 |
| `safetyAssertions` | 无模型环境下也必须成立的安全断言 |

必须覆盖的场景：`standard`、`synonym`、`combination`、`negation`、`history`、
`family`、`uncertain`、`multi_document`、`granularity`、`conflict`、
`no_evidence`、`procedure_mix`。

## 指标

- Fact Accuracy / Negation Accuracy / Temporality Accuracy / Normalization Accuracy
- Evidence Sufficiency
- Top-1 Coding Accuracy / Top-3 Recall
- Hallucination Rate
- P50 / P95 延迟
- Trace 完整率
- 安全结论：无证据不编码、规则失败不安全推荐、已确认 Final Coding 不被覆盖

## 运行

```bash
dotnet run --project tools/HospitalAi.Tools -- golden --validate
dotnet run --project tools/HospitalAi.Tools -- evaluate --connection "<conn>" --golden data/phase1/golden/golden-dataset.json
```

评测使用独立临时医院与就诊数据，不触碰生产病历正文；正文只写入数据库与受控审计存储，
不进入普通应用日志。
