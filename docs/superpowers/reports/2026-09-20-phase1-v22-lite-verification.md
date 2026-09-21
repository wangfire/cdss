# Phase 1 智能编码推荐 V2.2-Lite 分片重构实施与验证报告

**日期：** 2026-09-20
**范围：** 按 `docs/superpowers/Phase1_智能编码推荐_本地模型化部署_开发实施基线_V2.2.md` 完成 V2.2-Lite 全部切片：三个硬冲突修复、7 张新表 + 9 张表扩展、11 步流水线、模型网关、ES BM25 降级、规则引擎、七维评分、软失效与 Legacy 只读、11 个新 API、compose 接入 Elasticsearch、回填与离线评测工具、上线前清理。
**前置报告：** `2026-09-18-phase1-rbac-otel-supplement.md`
**基线分支：** `codex-phase2-coding-mvp`（在既有未提交改动之上增量实施，未回退、未覆盖任何既有改动）

---

## 1. 三个硬冲突的修法

这三个是原 MVP 的结构性缺陷，V2.2 任何一步都踩得到，因此最先处理。

### 1.1 Trace 查询不能使用任务级 `Single`

旧实现在同一任务上查询 `pipeline_trace` 时假设任务只有一条 Trace。V2.2 里一次任务可以重跑、一次执行可覆盖多个诊断输入，任务与 Trace 是一对多。

- `src/HospitalAi.Contracts/CodingTasks/PipelineVersions.cs`：版本常量与判定，`Legacy = "phase2-mvp-legacy"` / `Lite = "phase1-v2.2-lite"` / `Full = "phase1-v2.2-full"`。
- 查询侧一律按 `CodingTaskId` 返回集合，由调用方选择最新一条；`GET /api/v1/traces/{traceId}` 按 traceId 主键取，天然避开歧义。

### 1.2 旧推荐不能物理删除

软失效要求同一 `(coding_task_id, diagnosis_input_id, recommendation_type, code)` 在重跑后可以同时存在旧版本（STALE）与新版本（ACTIVE）。这与旧 MVP 的 4 列唯一索引直接冲突，重跑必然撞唯一键。

- `src/HospitalAi.Infrastructure/SqlServer/HospitalAiDbContext.cs`：把唯一索引按 `recommendation_version` 拆成两条过滤索引。
  - 旧行（`recommendation_version IS NULL`）沿用原 4 列索引，行为不变。
  - V2.2 行（`recommendation_version IS NOT NULL`）用含版本列的 5 列索引，允许同编码多版本共存。
- `src/HospitalAi.Infrastructure/SqlServer/Migrations/20260920082800_V22LiteRecommendationVersionIndex.cs`：只做索引重建，不动数据、不删列。

### 1.3 Runner 必须按 PipelineVersion 选择

原 Dispatcher 用 `is V22LiteCodingPipelineRunner` 类型判断，且注册缺失时静默回退到第一个 Runner。这意味着一旦没配上 V2.2 实现，新任务会悄悄走旧 SQL 推荐逻辑。

- `src/HospitalAi.Worker/Pipeline/ICodingTaskPipelineCapabilities.cs`（新增）：Runner 自己声明 `SupportedPipelineVersions`。
- `src/HospitalAi.Worker/Pipeline/V22LiteCodingPipelineRunner.cs`：声明 `Lite` + `Full`（语义召回与重排由模型网关是否启用来决定，不按版本号分裂执行路径）。
- `src/HospitalAi.Worker/Pipeline/SqlServerCodingRecommendationPipelineRunner.cs`：只声明 `Legacy`，明文拒绝新 V2.2 任务。
- `src/HospitalAi.Worker/Pipeline/CodingTaskPipelineDispatcher.cs`：只认声明。没有执行器声明支持该版本时**抛异常拒绝执行**，禁止任何"模型不可用"式降级回退。

---

## 2. 数据模型

新增 7 张表（`clinical_fact` / `clinical_evidence` / `clinical_fact_evidence` / `coding_diagnosis_input` / `coding_candidate` / `recommendation_score` / `quality_issue`），扩展 9 张（`medical_document` / `document_section` / `clinical_entity` / `coding_recommendation` / `coding_review` / `final_coding_result` / `pipeline_trace` / `pipeline_trace_step` / `audit_log`），新增 `coding_stage` 状态列与 `pipeline_trace_step` 的 11 个新列。

所有持久化记录集中在 `src/HospitalAi.Infrastructure/SqlServer/PersistenceRecordsV22.cs`，映射集中在 `HospitalAiDbContext.cs`。新增列一律 nullable，不删旧列旧表，后续再逐步收紧。

本次会话新增两个迁移：

| 迁移 | 内容 |
|---|---|
| `20260920075505_V22LiteReviewResultType` | `coding_review.result_type`（ACCEPTED / REJECTED / MODIFIED），`nvarchar(64)` nullable |
| `20260920082800_V22LiteRecommendationVersionIndex` | 见 §1.2 的索引拆分 |

两个迁移均已通过 `dotnet ef migrations has-pending-model-changes` 校验，模型与迁移一致。

---

## 3. 11 步流水线

`src/HospitalAi.Worker/Pipeline/V22LiteCodingPipelineRunner.cs` 一个 Runner 内顺序执行 11 步，每步都写 `pipeline_trace_step`：

`QUALITY_GATE` → `DOCUMENT_VERSION` → `CHUNK` → `FACT` → `EVIDENCE` → `EXACT_RETRIEVAL` → `BM25_RETRIEVAL` → `RULE` → `SCORE` → `POLICY` → `PERSIST`

关键行为：

- 无诊断输入 / 无证据 / 规则阻断 → 不产出安全推荐，改写 `quality_issue` 与 `CodingStage`，而不是硬给一个低分候选。
- Facts / Evidence / Candidates 全部按 `pipeline_run_id` 归属本次运行。
- 重跑时旧推荐标 `STALE`、旧证据与候选**物理保留**。
- 已确认的 Final Coding 不被自动重跑覆盖。
- Legacy 推荐（`is_read_only` 或 `LEGACY_READ_ONLY`）只读，编辑路径直接拒绝。

---

## 4. 检索与模型网关

- `src/HospitalAi.Infrastructure/Elasticsearch/`：ES BM25 HTTP 客户端 + 索引构建 + `UnavailableCodingKnowledgeSearch` 降级实现。ES 关闭时流水线走 Exact 快路径；Exact 未命中直接 `NO_SAFE_RECOMMENDATION`，**不回退 SQL 全量扫描**。
- `src/HospitalAi.Infrastructure/ModelGateways/`：`UnavailableModelGateway`（默认）、`RecordingModelGateway`（测试）、`OpenAiCompatibleModelGateway`（V2.2-Full 预留）、`SimpleJsonSchemaValidator`、`StaticModelRouter`。默认 `ModelMode=unavailable`，未接通模型时明确报告不可用，不伪造任何分数。
- `src/HospitalAi.Infrastructure/CodingKnowledge/SqlServerExactCodingKnowledgeSearch.cs`：Exact 快路径，只做标准化相等比较，不遍历全表、不用 `Contains` 当最终推荐。

### 4.1 修掉的两个真实缺陷

**ClinicalFactExtractor 的标题污染（阻塞性）**：`src/HospitalAi.Application/Coding/Preprocessing/ClinicalFactExtractor.cs` 原来只按顿号/分号切分，句末标点不切、段落标题不剥。`"【出院诊断】左胫骨平台粉碎性骨折。"` 会产出 `FactName = "出院诊断左胫骨平台粉碎性骨折"`，与编码库标题做标准化相等比较永远不成立 —— 整条 Exact 召回路径实际是死的。已加入句末标点切分（`FactSeparators`）与标题/条目编号剥离（`StripLeadingHeading`）。

**规则阻断问题不落结构化编码**：`V22LiteCodingPipelineRunner.CreateQualityIssue` 原来只把被阻断的编码写进描述文本，`quality_issue.current_code` 恒为空，人工复核无法按编码筛选和统计。已补 `currentCode` 参数并在阻断分支写入。

---

## 5. 规则引擎与评分

- `src/HospitalAi.Application/Coding/Rules/RuleEngine.cs`：版本化 JSON 条件（`all` / `any` / `not` + 类型化比较算子）求值，阻断优先于加权。条件不可解析时**不得命中**（宁可漏，不可误放）。
- `src/HospitalAi.Application/Coding/Scoring/ScoringOptions.cs`：`SevenDimensionScorer` 七维评分，权重全部来自配置（`Coding:Scoring:*`，Exact 0.15 / Semantic 0.15 / Retrieval 0.10 / Rerank 0.10 / Rule 0.15 / Evidence 0.20 / LLM 0.15），业务代码无硬编码权重。缺失维度不填默认值，按实际参与维度重新归一化。
- `LlmConsistencyScorer`：模型不可用或输出未通过 Schema 校验时返回 `null`，**不伪造 SemanticScore / LlmScore**。
- `RecommendationPolicy`：`HIGH_CONFIDENCE` 要求模型可用 **且** 输出已校验 **且** 证据分过阈值；无证据 → 不出安全推荐；规则阻断 → 不出安全推荐；无向量召回且证据不足 → `NEED_REVIEW` / `HUMAN_REQUIRED`。
- 不动态执行任意表达式或脚本。

### 5.1 LLM_OUTPUT_INVALID

模型输出必须通过 Schema 校验，最多重试 2 次。重试耗尽时分两种情况处理，不能混为一谈：

| 情况 | 标记 |
|---|---|
| 模型未接通 / 始终无响应 | `DEGRADED.NO_MODEL` |
| 模型有响应但输出始终不合法 | `DEGRADED.LLM_OUTPUT_INVALID` + `quality_issue`（`LlmOutputInvalid`，新增枚举值 3） |

`src/HospitalAi.Worker/Pipeline/V22LiteCodingPipelineRunner.cs` 的 `GetValidatedExplanationAsync` 因此返回 `ModelExplanationOutcome(Check, OutputInvalid)` 而不是可空的 check：把"输出不合法"折叠成"模型不可用"会掩盖质量问题，复核侧看不到模型侧已经出了问题。无论哪种情况，该次输出都不进入任何评分口径，也不得产生 `HIGH_CONFIDENCE`。

日志侧同步收紧：模型输出的 Schema 错误只记条数，不记原文，避免 AI 响应进入普通应用日志。

---

## 6. API 与前端契约

`src/HospitalAi.Api/Program.cs` 新增 11 个端点，全部套 `ApiEnvelope`，全部按 `X-Hospital-Id` / JWT `hospitalId` 隔离：

| 方法 | 路径 |
|---|---|
| POST | `/api/v1/coding/tasks/{taskId}/diagnoses:batch` |
| POST | `/api/v1/coding/diagnosis-inputs/{id}/recommend` |
| GET | `/api/v1/coding/diagnosis-inputs/{id}/recommendations` |
| GET | `/api/v1/coding/recommendations/{id}` |
| POST | `/api/v1/reviews/{id}/accept` |
| POST | `/api/v1/reviews/{id}/reject` |
| POST | `/api/v1/reviews/{id}/modify` |
| POST | `/api/v1/final-results/submit` |
| GET | `/api/v1/coding-tasks/{taskId}` |
| GET | `/api/v1/coding-tasks/{taskId}/recommendations` |
| GET | `/api/v1/workbench/tasks` |

审核三动作写入 `coding_review.result_type`；`POST /api/v1/admin/knowledge/index/rebuild` 触发知识索引重建。

---

## 7. 基础设施与工具

- `deploy/docker-compose.dev.yml`：新增 `elasticsearch`（8.14.3 单节点，安全与 ml 关闭，`9201:9200`，健康检查 `wait_for_status=yellow`）。Worker 依赖其 `service_healthy`；`Elasticsearch__Enabled` 由 `${HOSPITAL_AI_ELASTICSEARCH_ENABLED:-true}` 控制，关闭时自动降级 Exact 快路径。
- `tools/HospitalAi.Tools/Backfill/BackfillCommand.cs`：Legacy 数据回填，`dry-run` / `apply` / `verify` 三种模式，永不删除数据。
- `tools/HospitalAi.Tools/Evaluation/` + `data/phase1/golden/golden-dataset.json`：离线评测脚手架，输出 Fact Accuracy / Negation Accuracy / Top-1 Accuracy。
- 默认 PipelineVersion 来自配置 `Coding:Pipeline:DefaultPipelineVersion`（Api 与 Worker 的 `appsettings.json` 均为 `phase1-v2.2-lite`），代码不硬编码灰度开关。

---

## 8. 上线前清理清单核验

| 项 | 状态 |
|---|---|
| 删除 `Program.cs` 中硬编码医院 GUID 的临时重触发端点 | 已删除，全仓检索无残留 |
| 清理根目录 `temp_retrigger_recommendations.sql` | 已删除 |
| 清理生产日志中的病历正文 | `RequestLoggingMiddleware` 不读请求体，只记结构化字段；临时正文草稿文件已从仓库根目录清除 |
| 所有新 API 使用 `ApiEnvelope` | 已核验（11/11） |
| 所有数据库查询带 `hospital_id` | `SqlServerV22CodingService` 内 32 处 `HospitalId` 过滤 |
| 所有 AI 输出经过 Schema 校验 | `SimpleJsonSchemaValidator`，重试上限 2 次后 `LLM_OUTPUT_INVALID` |
| 所有 Legacy 结果只读 | `is_read_only` / `LEGACY_READ_ONLY` 双判，编辑路径拒绝 |
| 默认 PipelineVersion 来自配置 | 已核验 |
| 现有未提交用户改动未被覆盖 | 增量实施，未回退 |

---

## 9. 验证状态

### 9.1 已执行

```
dotnet build HospitalAi.slnx        → 0 error / 0 warning
dotnet test  HospitalAi.slnx        → 89 passed, 0 failed, 0 skipped
  HospitalAi.Domain.Tests         18 passed
  HospitalAi.Application.Tests    21 passed
  HospitalAi.Infrastructure.Tests 13 passed
  HospitalAi.Api.Tests             8 passed
  HospitalAi.Worker.Tests         29 passed
```

集成测试通过环境变量 `HOSPITAL_AI_TEST_CONNECTION_STRING` 指向 LocalDB，每个用例独立建库 `HospitalAi_V22_{Guid:N}`（`EnsureCreatedAsync`），用后 `SET SINGLE_USER ROLLBACK IMMEDIATE` 删除，互不污染。

### 9.2 出口条件覆盖

`tests/HospitalAi.Worker.Tests/V22LitePipelineTests.cs`（29 个）逐条对应阶段 1 出口条件：

| 出口条件 | 用例 |
|---|---|
| 无 GPU 环境可端到端运行 | `命中编码_产出候选七维分数证据与11个Trace阶段` |
| 无模型进入 `NEED_REVIEW` / `HUMAN_REQUIRED` | `Policy_模型不可用时不给出高置信`、`Policy_无向量召回且证据不足时降级为人工复核` |
| 无模型不得产生 `HIGH_CONFIDENCE` | `Policy_模型不可用时不给出高置信` |
| 无 Evidence 不产生安全推荐 | `Policy_无证据时不编码` |
| 规则阻断测试通过 | `规则阻断_候选被丢弃并记录质量问题`、`Policy_规则阻断时不给出安全推荐`、`规则引擎_*` |
| ES 故障可走 Exact 降级 | `Bm25不可用时_声明不可用且不返回任何命中` |
| 旧任务不能编辑 | `重跑_不覆盖已确认的最终编码` |
| 文档重跑不物理删除历史证据 | `重跑_旧推荐软失效且旧证据与候选保留` |
| Trace 完整率 100% | `命中编码_产出候选七维分数证据与11个Trace阶段`（11 阶段逐个断言） |
| 模型输出校验失败有明确标记 | `模型输出Schema校验失败_标记LLM_OUTPUT_INVALID且不得采信` |
| 冒烟链路全绿 | `tests/HospitalAi.Api.Tests/ApiSmokeTests.cs`（8 个） |

### 9.3 未验证 / 需本地执行

- **Docker 编排未启动**：本机当前没有运行中的容器，`docker compose -f deploy/docker-compose.dev.yml up` 未实际执行。ES 服务定义、健康检查与依赖顺序已按配置核验，但真实容器互联与 BM25 端到端检索未跑过。
- **`appsettings.Development.json` 的 ES 开关未在真实集群上验证**：`Elasticsearch__Enabled=true` 时的索引写入与 BM25 命中只经过了降级路径与单测覆盖。
- **真实模型未接入**：`OpenAiCompatibleModelGateway` 只做了代码级实现，未连接任何真实端点；V2.2-Lite 的验收口径本就是"无模型安全降级"，模型相关指标（Top-1 等）需在阶段 2 用真实模型复测。
- **`data/phase1/golden/golden-dataset.json` 是种子集**，规模不足以支撑正式指标结论，Fact / Top-1 Accuracy 数字仅供回归参照。

---

## 10. 关键文件索引

| 层 | 文件 |
|---|---|
| Contracts | `CodingTasks/PipelineVersions.cs`、`CodingRecommendations/V22CodingContracts.cs`、`Models/ModelGatewayContracts.cs` |
| Domain | `CodingTasks/CodingStage.cs`、`CodingTasks/RecommendationLifecycleStatus.cs`、`CodingTasks/RecommendationOutcome.cs`、`CodingTasks/CodingWireValues.cs`、`CodingTasks/ClinicalFactEnums.cs` |
| Application | `Coding/Preprocessing/*`、`Coding/Evidence/EvidenceBuilder.cs`、`Coding/Rules/RuleEngine.cs`、`Coding/Scoring/ScoringOptions.cs`、`Abstractions/IV22CodingService.cs`、`Abstractions/ModelGateways.cs` |
| Infrastructure | `SqlServer/PersistenceRecordsV22.cs`、`SqlServer/HospitalAiDbContext.cs`、`CodingTasks/SqlServerV22CodingService.cs`、`CodingTasks/CodingTaskPipelineOptions.cs`、`CodingKnowledge/*`、`Elasticsearch/*`、`ModelGateways/*`、`SqlServer/Migrations/202609200*` |
| Worker | `Pipeline/V22LiteCodingPipelineRunner.cs`、`Pipeline/V22LiteRecommendationExecutor.cs`、`Pipeline/CodingTaskPipelineDispatcher.cs`、`Pipeline/ICodingTaskPipelineCapabilities.cs`、`Pipeline/V22PipelineOptions.cs`、`CodingTaskPipelineRegistration.cs` |
| Api | `Program.cs`（11 个新端点）、`ApiEnvelope.cs` |
| Tools | `Backfill/BackfillCommand.cs`、`Evaluation/*` |
| 测试 | `tests/HospitalAi.Worker.Tests/V22LitePipelineTests.cs`、`tests/HospitalAi.Worker.Tests/V22TestDatabase.cs` |
| 数据 | `data/phase1/golden/golden-dataset.json` |
| 部署 | `deploy/docker-compose.dev.yml` |
