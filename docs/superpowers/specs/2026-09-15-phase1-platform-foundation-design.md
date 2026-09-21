# Phase 1 平台底座设计

**日期：** 2026-09-15  
**范围：** 方案 B：最小可运行平台底座  
**依据：** `Phase1_智能编码推荐_本地模型化部署_开发实施基线_V2.2.md`

## 1. 目标与非目标

### 目标

第一阶段建立一个不依赖医院真实接口和本地模型、可通过 Docker Compose 启动的 .NET 10 平台内核，验证以下基础能力：

1. API 接收并查询医院、患者、就诊、文书和编码任务。
2. 业务写入与 Outbox 事件在同一数据库事务中提交。
3. Worker 通过 RabbitMQ 消费任务，支持重试、失败和幂等。
4. 任务状态、Pipeline Trace 和审计日志可查询。
5. SQL Server、Redis、RabbitMQ、API、Worker 具备健康检查。
6. Document、ClinicalFact、Evidence、Recommendation 以接口形式预留。

### 非目标

本阶段不实现：

- 医院 HIS/EMR/LIS/PACS 真实接入；
- OCR、Embedding、Reranker、Vector DB；
- 本地小模型、强推理模型和模型路由；
- Elasticsearch 检索；
- 真实编码字典和医学规则；
- 生产级多医院部署、容量验收和 AI 效果承诺。

## 2. 架构

采用模块化单体 + Worker，而不是微服务。API 负责同步命令、查询和鉴权；Worker 负责异步任务；SQL Server 是业务事实源；Redis 只做缓存和短期协调；RabbitMQ 承担异步消息；Outbox 保证业务事务与事件发布的一致性。

```text
Client
  -> HospitalAi.Api
      -> Application
          -> Domain
          -> Infrastructure.SqlServer
          -> Outbox
      -> Redis（缓存/幂等辅助）
      -> RabbitMQ
  -> HospitalAi.Worker
      -> Application / Domain
      -> SQL Server
      -> Audit / Pipeline Trace
```

后续 AI、RAG、OCR 只允许通过 Application 层接口接入，业务代码不得直接依赖具体模型、向量库或推理框架。

## 3. 工程结构

```text
src/
  HospitalAi.Api/
  HospitalAi.Application/
    Abstractions/
    Hospitals/
    Patients/
    Visits/
    Documents/
    CodingTasks/
    Pipeline/
    Audit/
  HospitalAi.Domain/
    Common/
    Hospitals/
    Patients/
    Visits/
    Documents/
    CodingTasks/
  HospitalAi.Infrastructure/
    SqlServer/
    Redis/
    RabbitMq/
    Outbox/
    Audit/
    Observability/
  HospitalAi.Worker/
  HospitalAi.Contracts/
tests/
  HospitalAi.Domain.Tests/
  HospitalAi.Application.Tests/
  HospitalAi.Infrastructure.Tests/
deploy/
  docker-compose.dev.yml
db/
  001_schema.sql
  002_indexes.sql
configs/
  appsettings.Development.json
  worker.json
```

## 4. 第一版领域边界

### Hospital

保存医院标识、名称、状态和审计时间。`hospital_id` 是所有业务资源的数据隔离边界。

### Patient

保存医院内患者标识和来源系统标识。第一版不实现跨医院患者合并。

### Visit

保存一次住院/门诊就诊及其时间范围，校验出院时间不得早于入院时间。

### MedicalDocument

保存文书元数据和版本号。第一版只接收文本占位，不接入 OCR；文书内容保存引用和哈希，避免业务表承载大文本。

### CodingTask

保存编码任务及状态：`PENDING`、`RUNNING`、`SUCCESS`、`FAILED`、`RETRYING`、`TIMEOUT`、`CANCELLED`、`HUMAN_REQUIRED`。

### PipelineTrace

保存任务级和步骤级追踪信息。第一版要求业务任务 Trace 全量记录；普通技术指标是否采样不影响业务 Trace。

## 5. 数据库

第一版建立以下表：

```text
hospital
app_user
app_role
app_user_role
patient
visit
medical_document
coding_task
pipeline_trace
pipeline_trace_step
outbox_message
inbox_message
audit_log
```

> `medical_document_version` 已合并进 `medical_document.version` 字段，通过唯一索引
> `ux_medical_document_visit_type_version` 保证同一就诊同一文书类型同一版本唯一。

通用约束：

- 主键使用 `uniqueidentifier`；
- 所有核心表包含 `hospital_id`、`created_at`、`updated_at`；
- 并发更新使用 `rowversion`；
- 业务唯一性通过数据库唯一索引保证；
- 外键使用显式约束；
- 生产敏感字段使用应用层加密或脱敏，禁止写入普通日志；
- Outbox 与业务表在同一事务中提交；
- Inbox 以 `message_id + consumer_name` 建唯一索引，保证消费者幂等。

## 6. API 最小契约

第一版先提供：

```http
GET  /api/v1/health
POST /api/v1/hospitals
POST /api/v1/patients
POST /api/v1/visits
POST /api/v1/documents
POST /api/v1/coding-tasks
GET  /api/v1/coding-tasks/{taskId}
GET  /api/v1/traces/{traceId}
```

统一要求：

- 请求头支持 `X-Request-Id`、`X-Trace-Id`、`X-Hospital-Id`、`Idempotency-Key`；
- 写接口返回资源 ID、状态和追踪号；
- 所有错误返回 `{ code, message, traceId, details }`；
- 重复幂等请求返回第一次请求的业务结果；
- 资源不属于当前医院时统一返回 404，避免泄露跨医院数据；
- API 不直接暴露数据库实体，使用 Contracts DTO。

## 7. 异步处理

第一版只启用 `coding.task.created` 队列，后续再扩展文书解析、Fact、Evidence 等队列。

处理流程：

```text
POST coding-task
  -> SQL 事务写 coding_task + outbox_message
  -> Outbox Publisher 发布 coding.task.created
  -> Worker 写 inbox_message
  -> Worker 将任务置为 RUNNING
  -> 执行业务占位处理
  -> 写 pipeline_trace_step / audit_log
  -> SUCCESS 或 RETRYING / FAILED
```

重试策略：最多 3 次，间隔 5 秒、30 秒、180 秒；超过次数进入 FAILED，并保留错误码。任务处理器必须以 `task_id + pipeline_version` 幂等。

## 8. 可观测性与安全

- Serilog 输出结构化日志；生产日志不得包含患者姓名、病历原文和模型 Prompt。
- OpenTelemetry 暴露 HTTP、SQL、RabbitMQ 和 Worker 指标。
- 健康检查区分存活检查与就绪检查。
- 审计记录操作者、医院、资源类型、资源 ID、动作、结果、请求号和时间。
- 第一版提供最小 RBAC 模型，但不宣称完成医院级权限矩阵；真实权限矩阵作为接入前门禁。

## 9. 测试策略

采用 TDD，先测试再实现：

- Domain：时间校验、状态转换、幂等键生成；
- Application：创建任务、重复请求、跨医院访问拒绝；
- Infrastructure：Outbox 原子写入、Inbox 唯一约束、失败重试；
- API：错误结构、请求追踪头、资源隔离；
- Smoke：Docker Compose 启动后健康检查和一条任务消息闭环。

## 10. 第一版验收标准

1. `docker compose up` 后 API、Worker、SQL Server、Redis、RabbitMQ 均进入 Ready。
2. 创建编码任务时，业务数据与 Outbox 消息在同一事务中落库。
3. Worker 能消费任务并将状态从 `PENDING` 变为 `RUNNING` 再到 `SUCCESS`。
4. 同一幂等键重复提交不会创建第二个任务。
5. 消费同一消息两次不会重复执行业务处理。
6. 模拟失败后按 5/30/180 秒策略重试，超过次数进入 `FAILED`。
7. 能根据 `traceId` 查询任务级 Trace、步骤 Trace 和审计记录。
8. 跨医院访问返回 404，日志中不出现患者姓名和原文。
9. Domain、Application、Infrastructure 和 Smoke 测试全部通过。
10. 后续接入 Document、Fact、Evidence、Recommendation 时不需要修改 API 主流程和消息基础设施。
11. RBAC 最小模型落地：`app_user`、`app_role`、`app_user_role` 三张表提供用户身份、角色定义和用户-角色分配能力。
12. OpenTelemetry 指标落地：API 暴露 HTTP/SQL/运行时指标，Worker 暴露 SQL/流水线执行指标，Prometheus 端点可抓取。

## 11. 后续门禁

在进入真实数据接入或 AI 开发前，必须由用户补充并确认：

- 医院系统接口协议与字段字典；
- 脱敏样例病案；
- ICD 编码版本和知识库数据；
- Golden Dataset 与标注规范；
- 模型、Embedding、Reranker、OCR 和 Vector DB POC 结果；
- 角色-资源-动作权限矩阵；
- 正式 SLA、容量和恢复目标。

