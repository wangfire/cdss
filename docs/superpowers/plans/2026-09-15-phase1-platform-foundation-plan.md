# Phase 1 平台底座实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**目标：** 建立可由 Docker Compose 启动的 .NET 10 模块化单体 + Worker 平台底座，验证任务创建、Transactional Outbox、RabbitMQ 消费、幂等、重试、Trace、审计、RBAC 用户/角色、OpenTelemetry 指标和健康检查。

**架构：** API 负责 DTO、请求追踪、命令和查询；Application 编排用例；Domain 承载状态转换和业务不变量；Infrastructure 负责 SQL Server、Redis、RabbitMQ、Outbox、Inbox、审计、RBAC 和 OpenTelemetry 观测；Worker 消费 `coding.task.created` 并执行编码流水线。AI、RAG、OCR 只以接口预留，不进入本阶段实现。

**技术栈：** .NET 10、ASP.NET Core Minimal API、EF Core 10、SQL Server、Redis、RabbitMQ、MassTransit、Serilog、OpenTelemetry、xUnit、FluentAssertions。

---

## 文件结构

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
    Security/        # RBAC 用户/角色/分配
  HospitalAi.Domain/
    Common/
    Hospitals/
    Patients/
    Visits/
    Documents/
    CodingTasks/
    Security/        # AppUser / AppRole / AppUserRole
  HospitalAi.Infrastructure/
    SqlServer/
    Redis/
    RabbitMq/
    Outbox/
    Audit/
    Observability/   # OpenTelemetry 指标扩展
    Security/        # RBAC 持久化实现
  HospitalAi.Worker/
    Observability/   # 流水线 Meter 指标
  HospitalAi.Contracts/
    Security/        # RBAC DTO
tests/
  HospitalAi.Domain.Tests/
  HospitalAi.Application.Tests/
  HospitalAi.Infrastructure.Tests/
  HospitalAi.Api.Tests/
  HospitalAi.Worker.Tests/
deploy/docker-compose.dev.yml
db/001_schema.sql
db/002_indexes.sql
```

## 已完成的 Task 1-9

Task 1（解决方案与工程边界）、Task 2（领域实体与状态转换）、Task 3（Contracts 与请求上下文）、Task 4（EF Core 模型与索引）、Task 5（Outbox/Inbox 与幂等）、Task 6（Application 服务与 API）、Task 7（Worker、RabbitMQ、重试与 Trace）、Task 8（健康检查、日志与 RBAC 基础）、Task 9（Docker Compose 冒烟环境）已在 Phase 1 完成。

## Task 10：RBAC 用户/角色/权限

**文件：**
- `src/HospitalAi.Domain/Security/AppUser.cs`、`AppRole.cs`、`AppUserRole.cs`
- `src/HospitalAi.Application/Security/ISecurityService.cs` + `SqlServerSecurityService.cs`
- `src/HospitalAi.Contracts/Security/SecurityContracts.cs`
- `src/HospitalAi.Infrastructure/SqlServer/PersistenceRecords.cs`（`AppUserRecord`、`AppRoleRecord`、`AppUserRoleRecord`）
- `src/HospitalAi.Infrastructure/SqlServer/HospitalAiDbContext.cs`（三表映射 + 唯一索引）
- `src/HospitalAi.Api/Program.cs`（RBAC 端点）
- `tests/HospitalAi.Domain.Tests/RbacTests.cs`
- `tests/HospitalAi.Application.Tests/RbacServiceTests.cs`

- [x] 先写失败测试：用户/角色创建幂等、分配后查询、跨医院资源访问返回 404、停用后状态变更。
- [x] 实现 Domain 实体 `AppUser`（含状态校验）、`AppRole`、`AppUserRole`。
- [x] 实现 `ISecurityService` 契约 + `SqlServerSecurityService` 实现，所有操作均按 `X-Hospital-Id` 隔离。
- [x] 注册 `app_user`、`app_role`、`app_user_role` 三张表，主键 `uniqueidentifier`、`row_version` 并发列、唯一索引 `ux_app_user_hospital_code` / `ux_app_role_hospital_code` / `ux_app_user_role_hospital_user_role`。
- [x] 在 API 新增 `POST /api/v1/users`、`POST /api/v1/roles`、`POST /api/v1/users/{userId}/roles/{roleId}`、`GET /api/v1/users/{userId}/roles`、`PATCH /api/v1/users/{userId}/status` 端点。
- [x] 运行 Domain 和 Application 的 RBAC 测试，预期全部 PASS。

## Task 11：OpenTelemetry 指标

**文件：**
- `src/HospitalAi.Infrastructure/Observability/ObservabilityServiceCollectionExtensions.cs`
- `src/HospitalAi.Worker/Observability/PipelineMetrics.cs`
- `src/HospitalAi.Worker/Consumers/CodingTaskCreatedConsumer.cs`（集成流水线指标）
- `src/HospitalAi.Worker/RabbitMqWorkerRegistration.cs`（启用 MassTransit `UseInstrumentation` Meter）
- `src/HospitalAi.Api/HospitalAi.Api.csproj`（OpenTelemetry 包引用）
- `src/HospitalAi.Worker/HospitalAi.Worker.csproj`（OpenTelemetry 包引用）
- `src/HospitalAi.Worker/Program.cs`（Worker 指标注册）
- `src/HospitalAi.Api/Program.cs`（API 指标注册 + Prometheus 端点 `/metrics`）

- [x] 添加 `AddHospitalAiApiMetrics` 扩展：注册 ASP.NET Core HTTP、SQL Client、运行时三个 instrumentation + Prometheus 导出器，过滤健康检查端点。
- [x] 添加 `AddHospitalAiWorkerMetrics` 扩展：注册 SQL Client、运行时 instrumentation + MassTransit Meter（RabbitMQ 消费）+ 自定义 `HospitalAi.Worker` Meter（流水线） + Prometheus 导出器。
- [x] 在 `CodingTaskCreatedConsumer` 中集成 `PipelineMetrics`，记录流水线执行/失败/结果。
- [x] API 暴露 `/metrics` 端点；Worker 通过 MassTransit 内置 Meter 暴露 RabbitMQ 消费指标，自定义 Meter 暴露流水线指标。
- [x] 运行全量测试，预期 OTel 集成不破坏现有行为。

## Task 12：Worker 死代码清理

**文件：**
- `src/HospitalAi.Worker/Worker.cs`

- [x] 删除原 Phase 1 Task 7 之前的脚手架 `Worker` BackgroundService，保留文件作占位注释，避免被 `Program.cs` 重新引入。

## Task 13：文档同步

- [x] `docs/superpowers/specs/2026-09-15-phase1-platform-foundation-design.md`：`.NET 8` → `.NET 10`；删除独立 `medical_document_version` 表条目并注明已合并进 `medical_document.version`；验收标准新增 RBAC 三表和 OTel 指标两项。
- [x] `docs/superpowers/plans/2026-09-15-phase1-platform-foundation-plan.md`：`.NET 8` → `.NET 10`，EF Core 8 → 10。
- [x] `docs/superpowers/Phase1_智能编码推荐_本地模型化部署_开发实施基线_V2.2.md`：技术栈行 `.NET 8` → `.NET 10`，Sprint 1 平台骨架 `.NET 8` → `.NET 10`。

## Task 14：最终验证与交付

- [ ] 运行 `dotnet format HospitalAi.slnx --verify-no-changes --no-restore`。
- [ ] 运行 `dotnet test HospitalAi.slnx --configuration Release --no-restore`。
- [ ] 运行 `pwsh -File tests/smoke/run.ps1`。
- [ ] 更新 `README.md`，记录 RBAC 端点、OTel Prometheus 端点和已知未交付项。
