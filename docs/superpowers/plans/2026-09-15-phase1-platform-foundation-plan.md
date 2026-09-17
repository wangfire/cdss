# Phase 1 平台底座实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**目标：** 建立可由 Docker Compose 启动的 .NET 8 模块化单体 + Worker 平台底座，验证任务创建、Transactional Outbox、RabbitMQ 消费、幂等、重试、Trace、审计和健康检查。

**架构：** API 负责 DTO、请求追踪、命令和查询；Application 编排用例；Domain 承载状态转换和业务不变量；Infrastructure 负责 SQL Server、Redis、RabbitMQ、Outbox、Inbox、审计和观测；Worker 消费 `coding.task.created` 并执行占位处理。AI、RAG、OCR 只以接口预留，不进入本阶段实现。

**技术栈：** .NET 8、ASP.NET Core Minimal API、EF Core 8、SQL Server、Redis、RabbitMQ、MassTransit、Serilog、OpenTelemetry、xUnit、FluentAssertions。

---

## 文件结构

```text
src/
  HospitalAi.Api/
  HospitalAi.Application/
  HospitalAi.Domain/
  HospitalAi.Infrastructure/
  HospitalAi.Worker/
  HospitalAi.Contracts/
tests/
  HospitalAi.Domain.Tests/
  HospitalAi.Application.Tests/
  HospitalAi.Infrastructure.Tests/
deploy/docker-compose.dev.yml
db/001_schema.sql
db/002_indexes.sql
configs/appsettings.Development.json
```

## Task 1：建立解决方案与工程边界

**文件：** 创建 `HospitalAi.sln`、六个生产项目、三个测试项目。

- [ ] 写入 `tests/HospitalAi.Domain.Tests/ProjectSmokeTests.cs`，断言测试项目可加载。
- [ ] 运行 `dotnet test tests/HospitalAi.Domain.Tests/HospitalAi.Domain.Tests.csproj`，确认项目尚不存在时按预期失败。
- [ ] 使用 `dotnet new` 创建项目并加入解决方案；依赖方向固定为 `Api -> Application/Contracts/Infrastructure`、`Worker -> Application/Contracts/Infrastructure`、`Infrastructure -> Application/Domain`、`Application -> Contracts/Domain`。
- [ ] 再次运行上述测试，预期 PASS。

## Task 2：领域实体与状态转换

**文件：** 创建 `src/HospitalAi.Domain/Common/Entity.cs`、`DomainException.cs`、`Visits/Visit.cs`、`CodingTasks/CodingTask.cs`、`CodingTaskStatus.cs`；测试放入 `tests/HospitalAi.Domain.Tests/VisitTests.cs` 和 `CodingTaskTests.cs`。

- [x] 先写失败测试：出院时间早于入院时间时 `Visit.Create` 抛出 `DomainException`。
- [x] 运行领域测试，确认因类型不存在而失败。
- [x] 实现最小领域类型：`Visit.Create` 做时间校验；`CodingTask` 实现 `Start`、`Succeed`、`Retry`、`Fail`，非法状态转换抛出 `DomainException`。
- [x] 补充 `PENDING -> RUNNING -> SUCCESS`、`RUNNING -> RETRYING`、`RETRYING -> FAILED` 和 `SUCCESS -> RUNNING` 拒绝测试。
- [x] 运行领域测试，预期全部 PASS。

## Task 3：Contracts 与请求上下文

**文件：** 创建 `src/HospitalAi.Contracts/Common/ErrorResponse.cs`、医院/患者/就诊/编码任务请求和响应 DTO，以及 `src/HospitalAi.Application/Abstractions/IRequestContext.cs`、`ICodingTaskService.cs`。

- [x] 先写序列化失败测试，验证错误响应使用 `code`、`message`、`traceId`、`details` 四个 camelCase 字段。
- [x] 运行 Application 测试，确认因 DTO 不存在而失败。
- [x] 实现 DTO 和接口，禁止 DTO 直接暴露 EF 实体。
- [x] 运行 Application 测试，预期 PASS。

## Task 4：EF Core 模型、迁移与索引

**文件：** 创建 `src/HospitalAi.Infrastructure/SqlServer/HospitalAiDbContext.cs`、实体配置、迁移、`db/001_schema.sql`、`db/002_indexes.sql` 和 `tests/HospitalAi.Infrastructure.Tests/SqlServerSchemaTests.cs`。

- [x] 先写失败测试，检查 `hospital`、`visit`、`coding_task`、`outbox_message`、`inbox_message`、`pipeline_trace`、`pipeline_trace_step`、`audit_log` 映射和唯一索引。
- [x] 运行 Infrastructure 测试，确认因 DbContext 不存在而失败。
- [x] 实现 `uniqueidentifier` 主键、`rowversion` 并发列、显式外键、`hospital_id` 隔离以及 Inbox `(message_id, consumer_name)` 唯一索引。
- [x] 生成可重复执行的 SQL 脚本并写入一个无敏感数据的开发医院种子。
- [x] 运行 Schema 测试，预期 PASS。

## Task 5：Outbox、Inbox 与幂等

**文件：** 创建 `OutboxMessage`、`OutboxPublisher`、`InboxMessage`、`InboxStore`、`IEventBus` 及 `OutboxInboxTests.cs`。

- [x] 先写失败测试：业务写入与 Outbox 同事务、重复 Inbox 消息不执行两次、任务幂等键为 `task_id + pipeline_version`。
- [x] 运行 Infrastructure 测试，确认因存储类不存在而失败。
- [x] 使用同一个 EF 事务写业务数据和 Outbox；Inbox 重复插入返回 `false`，不执行 Handler。
- [x] 运行测试，预期 PASS。

## Task 6：Application 服务与 API

**文件：** 创建医院、患者、就诊、文书和编码任务服务；创建 `src/HospitalAi.Api/Program.cs`、请求上下文中间件和异常处理中间件；测试 `CodingTaskServiceTests.cs`。

- [x] 先写失败测试：创建任务、重复 `Idempotency-Key`、跨医院查询返回 NotFound。
- [x] 运行 Application 测试，确认因服务和中间件不存在而失败。
- [x] 实现创建任务：状态为 `PENDING`，写入 Outbox，并返回任务 ID 与 Trace ID；所有查询使用 `IRequestContext.HospitalId`。
- [x] 实现 `GET /api/v1/health`、医院、患者、就诊、文书、编码任务创建、任务查询和 Trace 查询接口。
- [x] 运行 Application 和 API 测试，预期 PASS。

## Task 7：Worker、RabbitMQ、重试与 Trace

**文件：** 创建 `src/HospitalAi.Worker/Program.cs`、`Consumers/CodingTaskCreatedConsumer.cs`、`RetryPolicy.cs`、PipelineTraceService、AuditService 和处理器测试。

- [x] 先写失败测试：成功处理、瞬时失败重试、三次失败后 FAILED、Trace 步骤和审计落库。
- [x] 运行处理器测试，确认因 Consumer/Processor 不存在而失败。
- [x] 实现 `PENDING -> RUNNING -> SUCCESS`；重试间隔为 5/30/180 秒，耗尽后写错误码并进入 FAILED；处理前检查 Inbox。
- [x] 配置 MassTransit 持久队列 `coding.task.created`、关联 Trace ID、重试策略和死信行为。
- [x] 运行处理器测试，预期 PASS。

## Task 8：健康检查、日志与 RBAC 基础

**文件：** 创建观测扩展、审计日志实体、RBAC 策略和请求上下文测试。

- [x] 先写失败测试：传播 `X-Request-Id`、生成 `X-Trace-Id`、医院隔离、日志不包含患者姓名。
- [x] 运行测试，确认中间件和观测扩展不存在时失败。
- [x] 用 Serilog 结构化属性记录日志，禁止记录患者姓名、病历原文和 Prompt。
- [x] 增加存活检查和就绪检查；就绪检查 SQL Server、Redis、RabbitMQ，存活检查只确认进程正常。
- [x] 运行 `dotnet test`，预期全部 PASS。

## Task 9：Docker Compose 冒烟环境

**文件：** 创建 `deploy/docker-compose.dev.yml`、`configs/appsettings.Development.json`、`.env.example`、`README.md` 和 `tests/smoke/run.ps1`。

- [x] 先写冒烟脚本：启动 Compose，轮询 `/api/v1/health/ready`，创建开发医院和编码任务，轮询至 SUCCESS；确认 Compose 尚不存在时失败。
- [x] 定义 SQL Server、Redis、RabbitMQ、API、Worker，使用命名卷、健康检查、开发凭据和内部网络。
- [x] 文档化 `docker compose -f deploy/docker-compose.dev.yml up --build`、就绪地址、开发凭据和停止命令。
- [x] 运行冒烟测试，预期 API/Worker Ready 且任务达到 SUCCESS。

## Task 10：最终验证与交付

**文件：** 更新 `README.md`，创建 `docs/superpowers/reports/2026-09-15-phase1-platform-foundation-verification.md`。

- [x] 运行 `dotnet format HospitalAi.slnx --verify-no-changes`。
- [x] 运行 `dotnet test --configuration Release`。
- [x] 运行 `pwsh -File tests/smoke/run.ps1`。
- [x] 记录命令、通过/失败结果、服务版本和环境限制；明确尚未具备真实医院/AI 接入条件。
- [x] 完成后提交：`git add .`、`git commit -m "feat: establish phase1 platform foundation"`。
