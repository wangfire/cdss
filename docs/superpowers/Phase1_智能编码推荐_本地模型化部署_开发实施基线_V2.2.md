# Phase 1 智能编码推荐开发实施基线 V2.2
## ——面向 Phase 2 全病历智能质控 / Agent智能分析 / Phase 3 平台治理的本地模型化部署方案

**文档性质：** 可直接用于研发、测试、实施、项目管理的开发实施基线（已融合第三轮架构评审意见，修订记录见 §1）  
**当前实施范围：** Phase 1 智能编码推荐  
**总体架构范围：** Phase 1 + Phase 2 + Phase 3  
**部署原则：** 医院内网、本地数据、本地模型、本地知识库、本地推理服务  
**技术栈：** .NET 10 / ASP.NET Core / EF Core 10 / SQL Server / Elasticsearch / Redis / RabbitMQ / 向量数据库 / OpenTelemetry / Serilog

---

# 1. 总体原则

> **修订记录 R3（2026-09-11，第三轮架构评审落实）**：
> ① 补回 coding_task、pipeline_trace / pipeline_trace_step 表（§8.3/§8.5）；
> ② 评分体系对齐 V2.1 §16（七维 + margin + FinalScore 公式，§8.3/§13.0）；
> ③ 硬件三档方案，Phase 1 冻结 reasoning 为 14B（§4.1/§5.2）；
> ④ 新增推理服务部署（§5.3）与在线延迟对冲（§4.5）；
> ⑤ quality_issue 字段化、Fact 生命周期字段补全（§8.2/§8.4）；
> ⑥ 补批量推荐与就绪查询 API（§17）；
> ⑦ model_registry / prompt_template 提前至 Phase 1 启用（§8.6）；
> ⑧ LLM 输出 Schema 校验（§4.4）、规则优先级/互斥组（§14）、业务状态转换表（§7.1）、原文进对象存储（§8.1）、Vector DB 选型（§26）、Sprint 8 压测口径修正（§19）、Data Quality Gate 定义（§3）、Fact 抽取 NER 分层（§4.2）。

本文件以已提供的 V2.1《AI + RAG 病案智能编码辅助决策与全病历质控系统》为基线，将总体架构进一步落实为可开发、可测试、可部署的 Phase 1 实施方案。

V2.1 已确定核心链路：

> 原始病历 → Clinical Fact → Evidence → 混合检索 → 规则约束 → LLM语义推理 → 编码候选 → 证据校验 → 人工确认 → 集合级诊断决策 → 全病历质控 → Feedback评测闭环。

必须遵守：
1. AI 推荐 ≠ 最终编码。
2. No Evidence, No Coding。
3. 硬规则优先于模型判断。
4. AI 推荐必须可解释、可追溯。
5. 人工确认结果优先级最高。
6. AI 不确定时允许拒答。
7. Agent 只能产生候选建议，不得直接写 Final Coding。
8. Model / Prompt / Knowledge / Rule / Coding Version 全部可追溯。

总体建设原则：

> **一套架构、三期建设、一期闭环。**

Phase 1 只实现智能编码推荐，但必须提前冻结 Phase 2/3 所需的数据边界、版本机制、模型路由、Rule/Agent 扩展点，避免后期推倒重构。

---

# 2. 三期业务边界

## 2.1 智能编码推荐——Phase 1

必须实现：
- 病案接入
- 文档解析
- 文档版本
- Chunk
- Clinical Fact
- Evidence
- Hybrid RAG
- Rule Engine
- 本地医疗小模型
- 本地强推理模型
- AI Model Gateway / Model Router
- Evidence Validation
- Coding Candidate
- Coding Recommendation
- 人工确认/修改/驳回
- Review
- Final Coding
- Pipeline Trace
- 基础 Feedback
- 文档增量更新
- 离线任务状态与兜底

## 2.2 全病历智能质控——Phase 2

在 Phase 1 底座上增加：
- 文档完整性
- 临床逻辑
- 编码逻辑
- 主要诊断
- 诊断排序
- 集合级 QC
- DRG/DIP 风险
- Quality Rule
- 分级审核
- RECODING

## 2.3 Agent 智能分析——Phase 2

增加：
- Agent Orchestrator
- Skill Registry
- Tool Calling
- 高风险触发
- 多轮推理
- Evidence Validation
- Human Handoff
- 预算/超时/最大轮次/终止条件

建议 Skill：
- AG001 主要诊断冲突分析
- AG002 漏诊断发现
- AG003 漏编码发现
- AG004 病理-诊断冲突
- AG005 手术-诊断冲突
- AG006 检验-诊断一致性
- AG007 治疗-诊断一致性
- AG008 编码粒度不足
- AG009 DRG/DIP 风险
- AG010 关键证据缺失
- AG011 多诊断关系
- AG012 文书完整性

## 2.4 平台治理——Phase 3

增加：
- A/B
- Gray Release
- 多模型
- 自动模型路由
- 知识治理
- 规则治理
- 高级 DRG/DIP
- 多医院
- 多租户
- 成本优化
- 高级知识图谱

---

# 3. 医院本地模型化总体架构

```text
HIS / EMR / LIS / PACS / 病理 / 手术麻醉 / DRG-DIP
                         |
                 Integration Gateway
                         |
                  Data Quality Gate
                         |
                Document / Structured
                         |
                Document Version
                         |
                       Chunk
                         |
                 Clinical Fact
                         |
          +--------------+--------------+
          |                             |
 Local Medical Small Model         Dictionary/Rule
          |                             |
          +--------------+--------------+
                         |
                     Evidence
                         |
                Hybrid RAG Retrieval
             /          |                   Exact         BM25       Vector
             \          |          /
                    Reranker
                         |
                Recommendation Policy
                         |
                  AI Model Gateway
                    /                       Small Model    Strong Reasoning
                    \          /
                     LLM Result
                         |
                Evidence Validation
                         |
                Coding Recommendation
                         |
                       Coder
                         |
                 Reviewer / Final
                         |
                Audit / Trace / Feedback
```

核心要求：
- 病历原文不出医院内网。
- Embedding、Reranker、LLM 全本地。
- 业务代码不绑定具体模型名称。
- 统一通过 Model Gateway 调用。
- Phase 2 Agent 复用同一个 Gateway。
- Phase 3 多模型通过路由配置扩展。

Data Quality Gate 检查项（接入时强制）：

- 必填字段完整性（患者/就诊/文书类型/时间）；
- 来源编码与系统字典匹配；
- 时间逻辑（入院 ≤ 出院、文书时间落在住院区间内）；
- 重复文书检测（source_document_id + content_hash）；
- 空文书/纯图片文书标记（转 OCR 流程）。

不通过的文书进入 HUMAN_REQUIRED 待办，不阻塞同病案其他文书的处理。

---

# 4. 本地模型部署参数

## 4.1 模型角色

| 角色 | 推荐规模 | Phase 1 | Phase 2 | 主要职责 |
|---|---:|---|---|---|
| medical-small | 7B/8B量级 | 必须 | 必须 | Fact、NER、否定、时间、归一化 |
| medical-reasoning | 14B 量级（Phase 1 冻结；32B 为 Phase 2 升级项，需 48GB 级显存，禁止在 24GB 单卡上规划） | 必须（14B） | 必须 | 复杂语义、复杂编码推理 |
| embedding | 0.5B~数B | 必须 | 必须 | 向量检索 |
| reranker | 数亿~数B | 必须 | 必须 | 候选重排 |
| OCR | 本地服务 | 必须 | 必须 | 扫描病历 |

模型品牌不在本基线中硬编码；最终通过医院真实病历 Golden Dataset + POC 冻结。

## 4.2 小模型任务

适合：
- 文档分类
- 医疗实体抽取
- Clinical Fact
- 否定识别
- 确定性
- 时间关系
- 当前/既往/家族史
- 术语标准化
- 候选召回
- Evidence 定位
- 初步冲突检测

分层建议：Fact/NER 不全部压给 7B LLM——**规则 + 轻量 NER 模型（BERT 类，可 CPU/低显存）处理约 80% 常规文书，7B 仅兜底复杂/低置信度 chunk**（与 V2.1 §29 模型分诊原则一致），离线吞吐可提升数倍。

## 4.3 强推理模型任务

适合：
- 多证据联合推理
- 复杂疾病语义
- 编码粒度判断
- 复杂候选排序
- 多文书冲突解释
- 拒答判断
- Phase 2 集合级诊断
- Phase 2 Agent 推理

不得直接：
- 写 Final Coding
- 覆盖人工确认
- 自动关闭 CRITICAL
- 自动解锁 HIGH
- 绕过 Evidence Validation

## 4.4 推理默认参数

小模型：

```yaml
temperature: 0
top_p: 0.8
max_tokens: 2048
timeout_ms: 15000
retry: 1
response_format: json
stream: false
concurrency: 4
```

强推理模型：

```yaml
temperature: 0
top_p: 0.8
max_tokens: 4096
timeout_ms: 30000
retry: 1
response_format: json
stream: false
concurrency: 1-2
```

以上是工程初始值，不是正式 SLA；正式值以 POC 和压测冻结。

所有需要程序消费的模型输出必须通过 JSON Schema 校验（Schema Registry 版本化，与 V2.1 §15.1 一致）：校验失败自动携带错误信息重试（最多 2 次，temperature 递减）；仍失败标记 LLM_OUTPUT_INVALID 并按降级处理，未经验证的输出不得进入业务。

## 4.5 在线延迟对冲（Phase 1）

单卡 24~48GB 下，整病案批量 P95 ≤ 60 秒（V2.1 §4.3）必须依赖以下三条对冲路径：

1. **提高 Exact 快速路径占比**：出院诊断经验上 60~80% 可精确命中，仅难例进入 LLM 推理；
2. **vLLM 常驻 + 连续批处理**：reasoning 并发可由保守值 1~2 提升至 8~16（14B 量级、延迟缓增），`concurrency: 1-2` 仅作为无连续批处理时的兜底；
3. **压缩推理上下文**：进入 LLM 的候选 top10 → top5、Evidence 8 → 6 条，输入 token 约减半。

以上路径的效果必须在 Sprint 0 的 Model Contract 与 POC 中量化验证；未达标则按 §5.2 三档方案升级硬件。

---
# 5. 硬件基线

## 5.1 当前开发机

现有 i7-9700 可继续承担：
- .NET
- SQL Server
- Redis
- RabbitMQ
- ES
- 前端
- 调试

模型推理不建议依赖 CPU。

## 5.2 硬件三档方案

显存实测参考（INT4/FP8 量化）：small 7B/8B ≈ 5~8GB；embedding ≈ 1~3GB；reranker ≈ 1~3GB；OCR ≈ 1~3GB（可 CPU）；reasoning 14B ≈ 8~10GB；reasoning 32B ≈ 18~20GB。

| 档位 | GPU | 可部署阵容 | 用途 |
|---|---|---|---|
| 开发机 | 单卡 24GB | small + embedding + reranker + OCR + reasoning 14B-Q4（紧凑无余量；或 reasoning 降级由 small 承担） | 开发联调，不做吞吐验收 |
| POC/试点 | 单卡 48GB 或 2×24GB | 全家桶 + reasoning 14B 常驻，预留 ≥ 20% 显存余量 | 阈值校准、延迟验收 |
| 生产 | 按日病案量压测扩容 | Phase 2 可升级 reasoning 32B（需 48GB 级显存） | SLA 达标 |

约束：

- RAM ≥ 128GB；NVMe 至少 2TB（建议系统与模型/数据分盘）；单 GPU 优先，多卡留给生产；
- 检查 PSU、PCIe、机箱空间和散热；
- 禁止在 24GB 单卡上规划 32B 推理模型（Q4 量化后约 18~20GB，无共存空间）。

## 5.3 本地推理服务部署

- 推理框架：**vLLM（OpenAI 兼容 API）**，挂接 Model Gateway；开发期可用 Ollama 联调，但**禁止用 Ollama 做吞吐验收与生产压测**（无连续批处理）；
- 显存配额：多模型常驻时按 `gpu_memory_utilization` 划分配额（如 24GB：small 0.35 / reasoning 0.45 / embedding+reranker 0.20），禁止默认互相挤占；
- 常驻策略：small / embedding / reranker / OCR 常驻；reasoning 在 POC 档及以上常驻，冷启动加载时间纳入就绪 SLA 计算；
- 健康检查：Gateway 对每个模型端点做探活 + 首 token 延迟探测；显存水位 > 90% 告警，> 95% 自动拒绝新批次并触发降级（V2.1 §34）；
- 所有推理请求/响应经 Gateway 审计；业务代码只依赖 model_code，不绑定具体模型名称。

---

# 6. .NET 工程结构

```text
src/
  HospitalAi.Api/
  HospitalAi.Application/
    Patient/
    Visit/
    Document/
    ClinicalFact/
    Evidence/
    RAG/
    Coding/
    Review/
    AI/
    Knowledge/
    Rule/
    Audit/
  HospitalAi.Domain/
  HospitalAi.Infrastructure/
    SqlServer/
    Elasticsearch/
    Vector/
    Redis/
    RabbitMq/
    LocalModel/
    Storage/
  HospitalAi.Worker/
    DocumentWorker/
    FactWorker/
    EvidenceWorker/
    EmbeddingWorker/
    CodingWorker/
  HospitalAi.Contracts/
  HospitalAi.Tests/
    Unit/
    Integration/
    AIRegression/
```

采用模块化单体 + MQ Worker。Phase 1 不拆大量微服务，Phase 2/3 按实际压力拆分。

---

# 7. 业务状态机

## 7.1 业务状态

```text
IMPORTED
 -> PROCESSING
 -> READY
 -> CODING
 -> AI_RECOMMENDING
 -> CODER_REVIEW
 -> COLLECTION_DECISION
 -> WHOLE_RECORD_QC
 -> REVIEW_REQUIRED
 -> FINAL_PENDING
 -> FINALIZED
 -> ARCHIVED
```

Phase 1 实际启用：

```text
IMPORTED -> PROCESSING -> READY
READY -> CODING
CODING -> AI_RECOMMENDING
AI_RECOMMENDING -> CODER_REVIEW
CODER_REVIEW -> FINAL_PENDING
FINAL_PENDING -> FINALIZED
```

Phase 2 再启用集合级 QC、Whole Record QC、REVIEW_REQUIRED、RECODING。

Phase 1 业务状态转换表：

| 来源 | 目标 | 触发 | 说明 |
|---|---|---|---|
| IMPORTED | PROCESSING | 离线任务开始 | Data Quality Gate 通过 |
| PROCESSING | READY | 离线任务全部 SUCCESS | 满足就绪 SLA（V2.1 §4.4） |
| PROCESSING | PROCESSING | 失败/重试 | 超 SLA 未就绪 → 工作台提示队列位置，可发起在线轻量处理 |
| READY | CODING | 编码员领案 | 创建 coding_task |
| CODING | AI_RECOMMENDING | 批量推荐提交（§17 diagnoses:batch） | 诊断级并行、渐进式返回 |
| AI_RECOMMENDING | CODER_REVIEW | 全部诊断出结果（失败可单独重试） | 单条失败不阻塞整体 |
| AI_RECOMMENDING | CODING | 编码员中止 | 已有结果保留 |
| CODER_REVIEW | FINAL_PENDING | 提交闸门校验通过 | §22 |
| CODER_REVIEW | CODER_REVIEW | CRITICAL 未闭环 | 阻断提交并提示处理 |
| FINAL_PENDING | FINALIZED | 审核完成（按风险分级） | Final Coding 落库 |
| FINALIZED | CODING | 显式 RECODING（Phase 2 启用） | Phase 1 不启用 |

## 7.2 技术任务状态

```text
PENDING
 -> RUNNING
 -> SUCCESS / FAILED / RETRYING / TIMEOUT / CANCELLED / HUMAN_REQUIRED
```

默认：
- max_retry = 3
- backoff = 5s / 30s / 180s
- 超过重试进入 DLQ
- 所有任务必须幂等

---

# 8. SQL Server 数据模型

## 8.1 基础表

### patient
```text
id
hospital_id
patient_no
patient_name
gender
birth_date
source_system
source_patient_id
created_at
updated_at
```

### visit
```text
id
hospital_id
patient_id
visit_no
visit_type
department_code
admission_time
discharge_time
attending_doctor_id
visit_status
coding_status
finalized_at
```

### medical_document
```text
id
hospital_id
visit_id
document_type
document_code
document_name
source_system
source_document_id
current_version
document_status
```

### medical_document_version
```text
id
document_id
version_no
storage_uri
raw_text
content_hash
source_updated_at
parser_version
ocr_version
is_current
```

storage_uri 指向对象存储中的原文；raw_text 仅开发期兼容保留，生产环境原文进对象存储、DB 留引用与 content_hash（避免大文本拖累备份与复制）。

### document_chunk
```text
id
document_version_id
chunk_no
section_type
text
start_position
end_position
token_count
content_hash
embedding_status
index_status
```

## 8.2 临床理解

### clinical_fact
```text
id
hospital_id
visit_id
fact_type
fact_name
normalized_value
original_value
negation
certainty
temporality
fact_status
confidence
extractor_model_version
source_document_version_id
source_chunk_id
source_start
source_end
created_at
```

fact_status：
- ACTIVE
- SUPERSEDED

superseded_by_fact_id、superseded_at 两字段随生命周期启用：历史推荐/质控问题保留对 SUPERSEDED Fact 的引用快照，新任务只消费 ACTIVE（V2.1 §10.2）。

### clinical_evidence
```text
id
hospital_id
visit_id
evidence_type
source_type
document_version_id
chunk_id
original_text
start_position
end_position
evidence_level
source_reliability
temporal_validity
text_completeness
evidence_score
```

### clinical_fact_evidence
```text
id
fact_id
evidence_id
relation_type
relation_confidence
```

## 8.3 编码

### coding_standard
```text
id
standard_code
standard_name
description
```

### coding_version
```text
id
standard_id
version_code
effective_from
effective_to
status
```

### coding_code
```text
id
coding_version_id
code
name
category
level
parent_id
is_billable
is_active
```

### coding_code_mapping
```text
id
coding_version_id
source_code
target_code
mapping_type
confidence
```

### coding_synonym
```text
id
coding_version_id
term
normalized_term
code
```

### coding_task
```text
id
hospital_id
visit_id
task_type
status
coding_version_id
priority
assigned_user_id
pipeline_version
created_at
started_at
completed_at
```

task_type：INITIAL_CODING / RECODING。

批量推荐以 coding_task 为提交单元（§17 diagnoses:batch）；coding_version_id 实现**任务级编码版本锁定**（V2.1 §32：在途病案不因字典更新而无感漂移）；单诊断推荐必须归属某个 coding_task。

### coding_diagnosis_input
```text
id
visit_id
source_type
original_text
normalized_text
is_principal
diagnosis_order
source_document_id
source_document_version_id
```

必须保存医生原始输入，AI 标准化不得覆盖。

### coding_candidate
```text
id
diagnosis_input_id
coding_version_id
code
name
recall_source
exact_score
bm25_score
vector_score
rerank_score
llm_score
rule_score
evidence_score
final_score
rank
candidate_status
```

分数字段分工：coding_candidate 保存召回/排序阶段的**过程分**（exact/bm25/vector/rerank 等各路原始分，用于 RAG 调试与评测）；进入综合评分的**规范七维**存 recommendation_score，final_score 以 recommendation 侧为准，candidate 侧分数仅作排序参考。

### coding_recommendation
```text
id
diagnosis_input_id
recommendation_version
outcome
recommended_code
confidence
final_score
score_profile
evidence_sufficiency
risk_level
reason
trace_id
pipeline_version
model_version
prompt_version
knowledge_version
rule_version
coding_version
```

Outcome（V2.1 decision_status 的 Phase 1 子集）：
- HIGH_CONFIDENCE
- NEED_REVIEW
- NO_SAFE_RECOMMENDATION

与 V2.1 七值枚举的映射：RECOMMEND → Phase 1 视同 NEED_REVIEW；CONFLICT / INSUFFICIENT_EVIDENCE / NO_MATCH / REJECT → 按触发原因归入 NO_SAFE_RECOMMENDATION 或 NEED_REVIEW；Phase 2 恢复完整七值枚举，两套枚举禁止混用。

### recommendation_score
与 V2.1 §16 对齐的七维 + margin：
```text
recommendation_id
exact_score
semantic_score
retrieval_score
rerank_score
rule_score
evidence_score
llm_score
margin_score
score_profile
```

margin_score = Top1 与 Top2 的 final_score 之差，参与推荐策略阈值（§13）；score_profile 记录评分口径（FULL / FAST / DEGRADED.*），跨口径分数不横向比较。

## 8.4 Review / Final

### quality_issue
```text
id
hospital_id
visit_id
diagnosis_input_id
issue_type
risk_level
fact_id
current_code
suggested_code
evidence_ids
description
confidence
status
resolved_by
resolved_at
```

issue_type（Phase 1 仅三种）：
- EVIDENCE_INSUFFICIENT
- RULE_VIOLATION
- GRANULARITY_INSUFFICIENT

risk_level：LOW / MEDIUM / HIGH / CRITICAL（由规则 DSL 的 risk 生成，提交闸门 §22 依赖此字段）；前端点击问题须能跳转 evidence_ids 对应原文。

### review_task
```text
id
visit_id
business_type
target_id
review_level
status
assignee_id
due_at
completed_at
```

### review_action
```text
id
review_task_id
action_type
before_code
after_code
before_text
after_text
reason_code
reason_text
evidence_ids
operator_id
created_at
```

review_action 承载结构化驳回原因与前后变更，是 Phase 2 Feedback 归因的直接数据源。

### coding_final_result
```text
id
visit_id
diagnosis_input_id
final_code
final_name
is_principal
diagnosis_order
coding_version_id
final_status
confirmed_by
confirmed_at
source_recommendation_id
```

Final Coding 是最终业务事实源，AI 自动重跑不得覆盖。

## 8.5 Pipeline Trace（Phase 1 启用）

### pipeline_trace
```text
id
hospital_id
trace_id
task_id
business_id
pipeline_version
start_time
end_time
duration
status
total_tokens
total_cost
```

### pipeline_trace_step
```text
id
trace_id
stage
start_time
end_time
duration
status
model_version
prompt_version
knowledge_version
rule_version
input_tokens
output_tokens
error_code
```

§15 要求的每步记录落地于这两张表；coding_recommendation.trace_id 关联 pipeline_trace。

## 8.6 AI 注册表（Phase 1 启用）

### model_registry
```text
id
model_code
model_role
provider
endpoint
version
status
```

model_role：small / reasoning / embedding / reranker / ocr；provider：local-vllm / local-ollama。

### prompt_template
```text
id
prompt_code
version
template_text
output_schema
status
effective_from
```

coding_recommendation 记录的 model_version / prompt_version 依赖这两张注册表解析——版本号没有注册表就无法回放，因此 **Phase 1 必须启用，不可推迟到 Phase 2**。

---

# 9. Phase 2/3 预建数据结构

Phase 1 可建表但不启用：

```text
clinical_fact_relation
diagnosis_collection
principal_diagnosis
diagnosis_order
collection_quality_issue
quality_rule
quality_rule_version
quality_rule_condition
quality_rule_action
quality_rule_scope
quality_issue_evidence
agent_skill
agent_task
agent_task_step
agent_tool
agent_budget
feedback
evaluation_case
evaluation_run
evaluation_result
golden_dataset
model_route_policy
knowledge_version
pipeline_version
audit_business
audit_ai
audit_data_access
external_sync_task
change_impact_analysis
```

所有核心表统一预留 `hospital_id`、版本和审计字段，为多医院/多租户做基础准备。model_registry 与 prompt_template 已提前至 Phase 1 启用（§8.6），不在预建清单中。

---

# 10. RAG 参数

```yaml
exact_match:
  enabled: true
  max_results: 20

bm25:
  enabled: true
  top_k: 50

vector:
  enabled: true
  top_k: 50

rerank:
  enabled: true
  top_k: 10
  minimum_score: 0.20

final_context:
  max_evidence: 8
  max_tokens: 6000
```

链路：

```text
QueryNormalizer
 -> ExactMatch
 -> GranularityGate
 -> BM25 + Vector
 -> Rerank
 -> Rule
 -> LLM
 -> Evidence Validation
 -> Score
 -> Recommendation
```

Embedding 必须保存：
- model_version
- dimension
- vector_id
- chunk_id
- content_hash

文档版本更新后旧向量失效，不允许新旧版本混检。

---

# 11. Clinical Fact 参数

Fact 至少表达：

```text
疾病/症状/体征/检查/检验/手术/治疗/用药
+
值
+
否定
+
确定性
+
时间
+
当前/既往
+
来源
+
置信度
```

示例：

```json
{
  "factType": "DIAGNOSIS",
  "factName": "肺炎",
  "normalizedValue": "肺炎",
  "originalValue": "考虑社区获得性肺炎",
  "negation": false,
  "certainty": "POSSIBLE",
  "temporality": "CURRENT",
  "confidence": 0.96,
  "source": {
    "documentVersionId": "...",
    "chunkId": "...",
    "start": 128,
    "end": 136
  }
}
```

必须同时保存 normalized / original / source span。

---

# 12. Evidence 参数

Evidence Level：

```text
A：明确诊断/病理/正式检查结论
B：明确病程、手术、影像、检验等直接证据
C：症状、体征、间接证据
D：推测性/可能性描述
E：弱相关上下文
```

初始权重：

```yaml
A: 1.00
B: 0.85
C: 0.65
D: 0.40
E: 0.20
```

Evidence Score：

```text
level_weight
× source_reliability
× temporal_validity
× text_completeness
```

该分数用于工程排序和安全控制，不等于医学真值概率。

---

# 13. Recommendation Policy

## 13.0 FinalScore 公式（与 V2.1 §16 对齐）

```text
FinalScore =
0.15 × Exact
+ 0.15 × Semantic
+ 0.10 × Retrieval
+ 0.10 × Rerank
+ 0.15 × Rule
+ 0.20 × Evidence
+ 0.15 × LLM
```

- 七维定义与取值来源见 V2.1 §16.1，分项存 recommendation_score（§8.3）；
- 缺维不填默认值，按参与维度权重占比重归一化；score_profile 记录评分口径（FULL / FAST / DEGRADED.*），跨口径不横向比较；
- margin_score = Top1 与 Top2 的 final_score 之差，参与下述阈值；
- 权重为工程初始值，Golden Dataset 校准后冻结。

阈值必须配置化：

```yaml
high_confidence:
  min_final_score: 0.85
  min_evidence_score: 0.75
  min_margin: 0.10
  require_rule_pass: true
  require_evidence: true

need_review:
  min_final_score: 0.60
  min_evidence_score: 0.50

no_safe_recommendation:
  max_final_score: 0.60
  evidence_insufficient: true
  conflict_unresolved: true
```

逻辑：

```text
Rule Fail -> NO_SAFE_RECOMMENDATION
No Evidence -> NO_SAFE_RECOMMENDATION
Conflict Unresolved -> NEED_REVIEW

Score >= 0.85
AND Evidence >= 0.75
AND Margin >= 0.10
AND Rule PASS
-> HIGH_CONFIDENCE
```

正式阈值必须用 Golden Dataset 校准。

---

# 14. Rule Engine

Rule 必须版本化：

```text
Rule
 └─ RuleVersion
     ├─ Condition
     ├─ Action
     ├─ Risk
     ├─ Blocking
     └─ TestCase
```

建议 DSL：

```json
{
  "ruleCode": "R-GRANULARITY-001",
  "priority": 10,
  "group": "GRANULARITY",
  "when": {
    "all": [
      {"field": "candidate.granularity", "op": "<", "value": "REQUIRED"}
    ]
  },
  "then": [
    {
      "action": "CREATE_QUALITY_ISSUE",
      "issueType": "GRANULARITY_INSUFFICIENT",
      "risk": "MEDIUM"
    }
  ]
}
```

优先级与互斥组（与 V2.1 §14.1 一致）：

- 同一编码命中多条规则时，先按 `group` 互斥组收敛（每组只保留组内 priority 数值最小者），再按剩余规则中 priority 最高者的判定执行；
- 内置规则（国家标准类）priority 恒高于医院自定义规则；
- 规则命中与裁决过程记入 pipeline_trace_step；
- 新规则上线前必须通过评测集回归，检测与现有规则的冲突。

Phase 2 用同一执行框架扩展 Q1~Q5。

---

# 15. AI Pipeline

单诊断：

```text
1 QueryNormalizer
2 ExactMatch
3 GranularityGate
4 Hybrid Retrieval
5 Rerank
6 RuleEngine
7 ModelRouter
8 LLM Reasoning
9 Evidence Validation
10 Score
11 Recommendation Policy
12 Save Candidate
13 Save Recommendation
14 Save Trace
```

每一步记录：

```text
trace_id
task_id
stage
start_time
end_time
duration
status
model_version
prompt_version
knowledge_version
rule_version
input_tokens
output_tokens
error_code
```

---

# 16. MQ / Outbox / 幂等

RabbitMQ + MassTransit。

队列：

```text
document.parse
document.chunk
fact.extract
evidence.build
embedding.generate
search.index
coding.recommend
coding.batch
feedback.process
evaluation.run
```

Phase 2：

```text
quality.scan
agent.execute
recoding.execute
```

必须采用 Transactional Outbox：

```text
DB Transaction
 ├─ Business Data
 └─ Outbox Event
       ↓
Publisher
       ↓
RabbitMQ
       ↓
Worker
       ↓
Inbox/Idempotency
```

幂等键：

```text
document_id + version + pipeline_version
diagnosis_input_id + recommendation_version
task_type + business_id + pipeline_version
```

---

# 17. API 契约

核心接口：

```http
GET  /api/v1/visits/{visitId}
GET  /api/v1/visits/{visitId}/documents
GET  /api/v1/visits/{visitId}/coding
GET  /api/v1/visits/{visitId}/readiness

POST /api/v1/documents
POST /api/v1/documents/{id}/versions

GET  /api/v1/visits/{visitId}/clinical-facts

POST /api/v1/coding/tasks/{taskId}/diagnoses:batch
POST /api/v1/coding/diagnosis-inputs/{id}/recommend
GET  /api/v1/coding/diagnosis-inputs/{id}/recommendations
GET  /api/v1/coding/recommendations/{id}

POST /api/v1/reviews/{id}/accept
POST /api/v1/reviews/{id}/reject
POST /api/v1/reviews/{id}/modify

POST /api/v1/final-results/submit
GET  /api/v1/visits/{visitId}/final-results
```

说明：

- `diagnoses:batch`：整病案批量并行推荐（V2.1 §4.3），以 coding_task 为提交单元，诊断级并行、渐进式返回；
- `readiness`：离线就绪状态与队列位置（V2.1 §4.4），未就绪时前端可发起在线轻量处理；
- `final-results/submit`：含提交闸门校验（§22）。

统一 Header：

```text
Authorization
X-Hospital-Id
X-Request-Id
X-Trace-Id
Idempotency-Key
```

错误：
- 400 参数错误
- 403 无权限
- 404 不存在
- 409 并发/版本冲突
- 503 依赖服务不可用

---

# 18. 前端工作台

三栏：

```text
+----------------+----------------------+------------------+
| 病历导航       | 当前诊断/编码        | Evidence / AI    |
| 文书列表       | 原始诊断             | Evidence         |
| 文书版本       | 候选编码             | 来源文书         |
|                | 推荐结果             | Rule             |
|                | Confidence           | AI解释           |
|                | 接受/修改/驳回       | Trace             |
+----------------+----------------------+------------------+
```

必须支持：
- Evidence 点击定位原文
- AI/人工结果对比
- 驳回原因
- 版本信息
- 文档变化提示
- HUMAN_REQUIRED 待办
- 离线任务状态
- 渐进式推荐展示

---

# 19. Sprint 开发规划

## Sprint 0：冻结研发合同
输出：
- API Contract
- Event Contract
- DB Dictionary
- Model Contract
- Pipeline Version
- 状态机
- 错误码
- 推理服务部署参数（§5.3：显存配额/常驻/健康检查）与延迟对冲验收口径（§4.5）

## Sprint 1：平台骨架
- .NET 10
- SQL Server
- Redis
- RabbitMQ
- Elasticsearch
- Vector
- OpenTelemetry
- Serilog
- Docker Compose
- RBAC

## Sprint 2：病案接入
- Integration Gateway
- Patient
- Visit
- Document
- Document Version
- Data Quality Gate
- Outbox

## Sprint 3：Document AI
- Parser
- Chunk
- OCR
- Embedding
- ES Index
- Vector Index

## Sprint 4：Clinical Fact / Evidence
- 本地小模型
- Fact Extraction
- Negation
- Temporality
- Certainty
- Normalization
- Evidence

## Sprint 5：Coding RAG
- ICD/Coding Standard
- Coding Version
- Code
- Synonym
- Mapping
- Exact
- BM25
- Vector
- Rerank
- Granularity Gate

## Sprint 6：AI Recommendation
- AI Gateway
- Model Router
- 强推理模型
- Prompt
- Rule Engine
- Evidence Validation
- Score
- Recommendation Policy

## Sprint 7：编码工作台
- Recommendation UI
- Evidence UI
- Review
- Reject Reason
- Final Coding
- Audit
- Trace

## Sprint 8：稳定性
- Golden Dataset
- AI Regression
- 全链路降级行为验证（低压下模拟 LLM/ES/Vector/DB 故障，验证 DEGRADED 路径与恢复）
- 吞吐压测（100/500/1000 并发）仅在生产规格硬件（§5.2 POC 档及以上）执行；开发机不做 LLM 吞吐验收
- MQ堆积
- LLM故障
- ES故障
- Vector故障
- DB慢查询
- Worker宕机
- 恢复演练

## Sprint 9：试点
- 小范围真实病案
- 辅助模式
- 指标采集
- 阈值校准
- 问题归因
- 版本冻结

---

# 20. Golden Dataset 与模型 POC

至少建立：
- Level 1 普通标准编码
- Level 2 复杂语义、否定、时间、既往史
- Level 3 多证据、多文书、多诊断
- Level 4 复杂粒度与冲突

模型 POC：
1. 医疗实体抽取
2. Clinical Fact
3. 否定/时间/确定性
4. 术语归一化
5. 编码语义判断

指标：

```text
Accuracy
Precision
Recall
F1
Fact Accuracy
Negation Accuracy
Temporality Accuracy
Normalization Accuracy
Evidence Sufficiency
Hallucination Rate
P50/P95 Latency
GPU Memory
GPU Utilization
Tokens/sec
Concurrent Requests
```

Phase 1 最关键指标：

> Clinical Fact Accuracy + Evidence Sufficiency + Coding Recommendation Accuracy。

---

# 21. 文档增量与版本

新文档版本：

```text
New Version
 -> New Chunk
 -> Old Chunk = SUPERSEDED
 -> Recheck Fact
 -> Recheck Evidence
 -> Unconfirmed Recommendation = STALE
 -> Re-run
```

已确认 Final Coding：
- 不自动覆盖
- 提示数据变化
- 高影响变化进入复核/RECODING

每次 Recommendation 必须可回放：

```text
Document Version
 -> Chunk
 -> Clinical Fact
 -> Evidence
 -> Retrieval
 -> Rerank
 -> Rule
 -> Prompt
 -> Model
 -> Recommendation
```

---

# 22. Final Coding 与审核

流程：

```text
AI Recommendation
      ↓
Coder
      ↓
Reviewer
      ↓
Final Coding
```

风险：
- LOW：编码员确认
- MEDIUM：编码员确认，必要时抽审
- HIGH：编码员 → 审核员
- CRITICAL：必须人工审核

提交闸门：
- CRITICAL 未闭环：阻断
- HIGH：必须二审解锁
- MEDIUM/LOW：提示

Final Coding 完成后，自动重跑不得覆盖。必须显式 RECODING，并产生新结果版本。

---

# 23. 安全与数据边界

全本地：

```text
病历原文       不出内网
Clinical Fact  不出内网
Evidence       不出内网
Embedding      不出内网
Vector         不出内网
Prompt         不出内网
LLM            不出内网
Trace          不出内网
```

必须：
- RBAC
- 医院范围
- 科室范围
- 数据访问范围
- AI 调用审计
- 数据访问审计
- 业务操作审计

生产病历禁止写入普通应用日志；模型训练必须经过授权、脱敏和数据治理。

---

# 24. 可观测性

监控：

```text
API latency
MQ backlog
Worker failure
LLM error rate
Token usage
GPU utilization
GPU memory
AI acceptance
Human modification
Evidence insufficiency
Top-1 Accuracy
P95/P99
Archive -> READY
DLQ
Cache hit
HUMAN_REQUIRED backlog
```

建议 Dashboard：
1. 平台健康
2. 数据接入
3. AI 推理
4. RAG
5. Coding Recommendation
6. 审核
7. GPU
8. MQ
9. DB
10. AI 质量

---

# 25. Redis 缓存

允许：
- Coding Dictionary
- Synonym
- Rule Version
- Model Metadata
- Prompt Metadata
- RAG Hot Query
- 临时 Recommendation

不允许 Redis 成为：
- Final Coding 唯一来源
- Audit 唯一来源
- Evidence 唯一来源

示例 Key：

```text
coding:version:{version}
coding:synonym:{version}:{term}
rule:version:{version}
model:route:{taskType}
recommend:{diagnosisInputId}:{pipelineVersion}
```

---

# 26. Docker 部署

开发环境：

```text
api
worker
sqlserver
redis
rabbitmq
elasticsearch
vector-db（Milvus / Qdrant 二选一，POC 冻结；须支持 metadata 过滤以满足 §10 的版本过滤）
object-storage
local-model-small
local-model-reasoning
embedding
reranker
otel-collector
```

生产建议：
- SQL Server 独立
- GPU/AI 节点独立
- ES 独立
- MQ 独立
- Object Storage 独立
- Monitoring 独立

目录：

```text
deploy/
  docker-compose.dev.yml
  docker-compose.ai.yml
  env/.env.example

db/
  001_schema.sql
  002_index.sql
  003_seed_coding.sql
  004_seed_rule.sql

configs/
  model-routing.yaml
  rag.yaml
  recommendation-policy.yaml
  worker.yaml

knowledge/
  coding/
  prompts/
  rules/
```

---

# 27. 初始工程参数

```yaml
api:
  timeout_ms: 30000
  max_page_size: 100

mq:
  max_retry: 3
  backoff_seconds: [5, 30, 180]

rag:
  exact_top_k: 20
  bm25_top_k: 50
  vector_top_k: 50
  rerank_top_k: 10
  evidence_max: 8

llm:
  small_timeout_ms: 15000
  reasoning_timeout_ms: 30000
  temperature: 0
  retry: 1

recommendation:
  high_confidence_score: 0.85
  evidence_min_score: 0.75
  min_margin: 0.10

worker:
  default_concurrency: 4
  llm_concurrency: 1-2

trace:
  sample_rate_dev: 1.0
  sample_rate_prod: 0.1
```

这些值是初始工程参数，不是未经压测的正式 SLA。

---

# 28. Phase 1 完成标准

## 数据
- Patient/Visit/Document/Version
- Fact/Evidence
- Coding Version / Coding Task
- Candidate/Recommendation/Score
- Review/Final Coding
- Pipeline Trace / Model Registry / Prompt Template

## AI
- 本地小模型
- 本地强推理模型
- Model Gateway
- Model Router
- Prompt Version
- Trace 回放

## RAG
- Exact
- BM25
- Vector
- Rerank
- Evidence Validation

## Rule
- Rule Version
- Rule Test
- Rule Runtime
- Rule Fail 控制

## 业务
- 单诊断推荐
- 批量推荐
- 人工接受
- 修改
- 驳回
- Final Coding
- 文档增量

## 工程
- MQ
- Outbox
- Retry
- DLQ
- Idempotency
- RowVersion
- Audit
- Monitoring

## AI 质量
- Golden Dataset
- Regression
- Fact Accuracy
- Evidence Sufficiency
- Coding Accuracy
- Latency

---

# 29. Phase 1/2/3 冲突检查

| 能力 | Phase 1 | Phase 2 | Phase 3 | 冲突风险 |
|---|---|---|---|---|
| Clinical Fact | 实现 | 复用扩展 | 复用 | 低 |
| Evidence | 实现 | 复用 | 复用 | 低 |
| RAG | 实现 | 扩展知识域 | 治理 | 低 |
| Rule Engine | 编码规则 | QC规则 | 治理 | 低 |
| AI Gateway | 实现 | 复用 | 多模型路由 | 低 |
| Small Model | 实现 | 复用 | 多模型 | 低 |
| Strong Model | 接入（14B冻结） | 扩大（可升32B） | 自动路由 | 低 |
| Recommendation | 实现 | 复用 | 多模型评测 | 低 |
| Quality Rule | 预建 | 启用 | 治理 | 低 |
| Agent | 接口预留 | 启用 | Skill治理 | 低 |
| Feedback | 基础 | 完整闭环 | 自动评测 | 低 |
| Evaluation | Golden Dataset | 平台化 | A/B/Gray | 低 |
| Microservice | 不拆 | 按需拆 | 平台化 | 低 |
| Multi-Hospital | hospital_id预留 | 扩展 | 正式多租户 | 低 |

禁止：
- Fact 简化为不可扩展 JSON
- 模型名称写死业务代码
- RAG 只支持单一路径
- Rule 写死 if/else
- Final Coding 等于 AI Recommendation
- 不做版本
- 不做 Outbox/Idempotency
- 不保存 Evidence/Trace
- Agent 另起数据链
- 后续再修改主表主键/医院边界
- 在 24GB 单卡上规划 32B 推理模型

---

# 30. 最终执行口径

> **总体架构一次设计，基础数据一次设计，AI能力统一接入，Phase 1 只把智能编码推荐做成生产闭环；Phase 2 在同一数据与AI底座上增加全病历质控和 Agent；Phase 3 在此基础上做模型、知识、规则、医院与租户治理。**

Phase 1 真正需要跑通的是：

> **病案接入 → 文档解析 → Clinical Fact → Evidence → Hybrid RAG → Rule → 本地小模型/强推理模型 → Evidence Validation → 编码推荐 → 人工确认 → Final Coding → Trace/Feedback。**

这条链稳定后，Phase 2 不需要推倒重来，而是在既有 Fact、Evidence、RAG、Rule、Model Gateway、Review、Audit 基础上继续建设。
