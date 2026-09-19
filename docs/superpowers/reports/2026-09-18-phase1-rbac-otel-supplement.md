# Phase 1 RBAC / OpenTelemetry 补齐验证报告

**日期：** 2026-09-18
**范围：** 6 项整改（RBAC 三表 + 权限功能、OTel 指标集成、Pipeline 注释、Worker 死代码清理、.NET 10 统一、medical_document_version 文档同步）的落地与验证
**前置报告：** `2026-09-15-phase1-platform-foundation-verification.md`、`2026-09-17-phase2-coding-mvp-verification.md`

---

## 1. 已交付内容

### 1.1 RBAC 最小模型（Task 1）

新增三张持久化表 `app_user` / `app_role` / `app_user_role`，提供用户身份、角色定义和用户-角色分配能力，所有操作按 `X-Hospital-Id` 隔离。

| 文件 | 变更 |
|---|---|
| `src/HospitalAi.Domain/Security/AppUser.cs` | 领域实体，工厂方法 `Create` + `SetStatus` 校验 `ACTIVE`/`DISABLED` |
| `src/HospitalAi.Domain/Security/AppRole.cs` | 领域实体，工厂方法 `Create` |
| `src/HospitalAi.Domain/Security/AppUserRole.cs` | 用户-角色关联，工厂方法 `Assign` |
| `src/HospitalAi.Application/Security/ISecurityService.cs` | 契约：`CreateUserAsync` / `CreateRoleAsync` / `AssignRoleAsync` / `GetRolesByUserAsync` / `SetUserStatusAsync` |
| `src/HospitalAi.Infrastructure/Security/SqlServerSecurityService.cs` | EF Core 实现，创建幂等、分配校验用户和角色存在、查询按医院隔离 |
| `src/HospitalAi.Contracts/Security/SecurityContracts.cs` | `CreateUserRequest` / `AppUserResponse` / `CreateRoleRequest` / `AppRoleResponse` DTO |
| `src/HospitalAi.Infrastructure/SqlServer/PersistenceRecords.cs` | `AppUserRecord` / `AppRoleRecord` / `AppUserRoleRecord`（含导航属性） |
| `src/HospitalAi.Infrastructure/SqlServer/HospitalAiDbContext.cs` | 三表映射 + 唯一索引 `ux_app_user_hospital_code` / `ux_app_role_hospital_code` / `ux_app_user_role_hospital_user_role`，FK Restrict |
| `src/HospitalAi.Api/Program.cs` | 5 个 RBAC 端点 + DI 注册 |
| `tests/HospitalAi.Domain.Tests/RbacTests.cs` | 8 个领域不变量测试 |
| `tests/HospitalAi.Application.Tests/RbacServiceTests.cs` | 5 个集成测试（创建幂等、分配查询、跨医院 404、状态变更） |

API 端点：

- `POST /api/v1/users` — 按 (hospital, code) 幂等创建用户
- `POST /api/v1/roles` — 按 (hospital, code) 幂等创建角色
- `POST /api/v1/users/{userId}/roles/{roleId}` — 分配角色，重复分配幂等
- `GET /api/v1/users/{userId}/roles` — 查询用户角色列表
- `PATCH /api/v1/users/{userId}/status` — 停用用户（设为 `DISABLED`）

### 1.2 OpenTelemetry 指标（Task 2）

按设计基线 §18 补齐 OTel 集成，覆盖 HTTP / SQL / 运行时 / RabbitMQ 消费 / 自定义流水线指标。

| 文件 | 变更 |
|---|---|
| `src/HospitalAi.Infrastructure/Observability/ObservabilityServiceCollectionExtensions.cs` | `AddHospitalAiApiMetrics`（AspNetCore + SqlClient + Runtime + Prometheus）/ `AddHospitalAiWorkerMetrics`（SqlClient + Runtime + MassTransit Meter + HospitalAi.Worker Meter + Prometheus） |
| `src/HospitalAi.Worker/Observability/PipelineMetrics.cs` | 自定义 `HospitalAi.Worker` Meter，`ProcessingCompleted` / `PipelineExecuted` / `PipelineFailed` 三个 Counter |
| `src/HospitalAi.Worker/Consumers/CodingTaskCreatedConsumer.cs` | 消费者中集成 `PipelineMetrics` 记录流水线执行/失败/结果 |
| `src/HospitalAi.Worker/RabbitMqWorkerRegistration.cs` | 移除不可用的 `UseInstrumentation()`（MassTransit 8.5.7 的 `IBusRegistrationConfigurator` 不支持），改为 OTel 侧 `AddMeter("MassTransit")` 采集内置 Meter |
| `src/HospitalAi.Api/Program.cs` | 注册 `AddHospitalAiApiMetrics("hospitalai.api")` + `UseOpenTelemetryPrometheusScrapingEndpoint()`（端点 `/metrics`） |
| `src/HospitalAi.Worker/Program.cs` | 注册 `AddHospitalAiWorkerMetrics("hospitalai.worker")` |
| `src/HospitalAi.Infrastructure/HospitalAi.Infrastructure.csproj` | 新增 OTel 包引用（Infrastructure 层持有，API/Worker 通过 ProjectReference 传递） |
| `src/HospitalAi.Api/HospitalAi.Api.csproj` | 新增 OTel 包引用（含 `OpenTelemetry.Extensions.Hosting` 1.10.0，`Prometheus.AspNetCore` 1.10.0-beta.1） |
| `src/HospitalAi.Worker/HospitalAi.Worker.csproj` | 新增 OTel 包引用 |

指标端点：

- API：`http://localhost:5080/metrics`（Prometheus 文本格式）
- Worker：`Microsoft.NET.Sdk.Worker`（非 ASP.NET Core），无 HTTP 端点；指标通过 `AddMeter("MassTransit")` + `AddMeter("HospitalAi.Worker")` 产生，开发环境可经 `dotnet-counters` 或 OTLP collector 抓取

### 1.3 Pipeline 注释（Task 3）

`src/HospitalAi.Worker/Pipeline/SqlServerCodingRecommendationPipelineRunner.cs` 类注释补上：

> coding_rule 表为预留，规则引擎消费在后续 Phase。当前流水线仅使用编码字典（MedicalCodes）与同义词（TermSynonyms）做召回，规则库未参与推荐打分。

### 1.4 Worker 死代码清理（Task 4）

`src/HospitalAi.Worker/Worker.cs` — 原 Phase 1 Task 7 之前的脚手架 `Worker` BackgroundService（每秒打印心跳日志、未被 `Program.cs` 注册）已删除，保留文件作占位注释，避免误引入。

### 1.5 .NET 10 统一（Task 5）

以下文档中 `.NET 8` → `.NET 10`、`EF Core 8` → `EF Core 10`：

- `docs/superpowers/specs/2026-09-15-phase1-platform-foundation-design.md`
- `docs/superpowers/plans/2026-09-15-phase1-platform-foundation-plan.md`
- `docs/superpowers/Phase1_智能编码推荐_本地模型化部署_开发实施基线_V2.2.md`

### 1.6 medical_document_version 文档同步（Task 6）

`docs/superpowers/specs/2026-09-15-phase1-platform-foundation-design.md` §5 数据库表清单：删除独立 `medical_document_version` 表条目，注明已合并进 `medical_document.version` 字段，唯一索引 `ux_medical_document_visit_type_version` 保证同一就诊同一文书类型同一版本唯一。

### 1.7 EF Core 迁移

`src/HospitalAi.Infrastructure/SqlServer/Migrations/20260918170609_RbacTables.cs` — `app_role` / `app_user` / `app_user_role` 三张表的建表迁移（主键、FK Restrict、唯一索引、row_version 并发列）。

---

## 2. 验证结果

### 2.1 编译

```text
dotnet build HospitalAi.slnx /v:m /nologo
已成功生成。
    0 个警告
    0 个错误
```

12 个项目（6 个 src + 5 个 test + 1 个 slnx）全部编译通过。

### 2.2 格式

```text
dotnet format HospitalAi.slnx --verify-no-changes --no-restore -v:m
（无输出 = 无格式差异）
```

4 处 whitespace 错误已用 `dotnet format` 自动修复后验证通过。

### 2.3 单元测试

```text
dotnet test tests/HospitalAi.Domain.Tests/HospitalAi.Domain.Tests.csproj
已通过! - 失败: 0，通过: 18，已跳过: 0，总计: 18
```

包含新增的 8 个 `RbacTests`（AppUser/AppRole/AppUserRole 领域不变量）+ 10 个既有 Domain 测试。

### 2.4 集成测试

使用独立 `docker run` 启动 SQL Server 2019 容器（`mcr.microsoft.com/mssql/server:2019-latest`，`MSSQL_SA_PASSWORD=HospitalAi2026`，宿主机端口 `14334`），设置 `HOSPITAL_AI_TEST_CONNECTION_STRING` 后运行全部测试项目：

```text
$env:HOSPITAL_AI_TEST_CONNECTION_STRING = 'Server=localhost,14334;Database=HospitalAiTests;User Id=sa;Password=HospitalAi2026;TrustServerCertificate=True;'
dotnet test tests/HospitalAi.Application.Tests/HospitalAi.Application.Tests.csproj
dotnet test tests/HospitalAi.Worker.Tests/HospitalAi.Worker.Tests.csproj
dotnet test tests/HospitalAi.Api.Tests/HospitalAi.Api.Tests.csproj
dotnet test tests/HospitalAi.Infrastructure.Tests/HospitalAi.Infrastructure.Tests.csproj
```

| 测试项目 | 结果 |
|---|---|
| HospitalAi.Domain.Tests | 18/18 通过 |
| HospitalAi.Application.Tests | 21/21 通过（含 `RbacServiceTests` 5 个集成测试） |
| HospitalAi.Worker.Tests | 6/6 通过（含 `CodingTaskCreatedConsumerTests` 5 个集成测试） |
| HospitalAi.Api.Tests | 7/7 通过（含 API 观测性测试） |
| HospitalAi.Infrastructure.Tests | 13/13 通过 |

**合计：65/65 通过，0 失败，0 跳过。**

> 注：原 Docker Compose 容器（`deploy_sqlserver-data` 卷）中 sa 登录失败，疑似历史卷残留导致 `MSSQL_SA_PASSWORD` 未正确应用。改用独立 `docker run` 容器后问题解决。

### 2.5 已知限制

1. **Worker 无 Prometheus HTTP 端点**：Worker 使用 `Microsoft.NET.Sdk.Worker`（非 ASP.NET Core），`UsePrometheusScrapingEndpoint` 是 ASP.NET Core 专用扩展。Worker 侧指标通过 `AddMeter("MassTransit")` + `AddMeter("HospitalAi.Worker")` 产生，需经 OTLP collector 或 `dotnet-counters` 抓取。
2. **`OpenTelemetry.Exporter.Prometheus.AspNetCore` 无 1.10.0 稳定版**：该包在 1.10.0 只有 `1.10.0-beta.1`（与核心 `OpenTelemetry` 1.10.0 GA 配套），已按此版本引用。
3. **`AddOpenTelemetry()` 扩展方法在 `OpenTelemetry.Extensions.Hosting` 包**：不是 `OpenTelemetry` 主包，Infrastructure.csproj 已显式引用。
4. **`AddAspNetCoreInstrumentation` 的 `Filter` 配置**：1.10.0 版本不支持 `Action<AspNetCoreMetricsOptions>` 重载，改为无参调用（健康检查端点过滤后续可通过 `MeterProviderBuilder.AddMetricFilter` 或 `AddAspNetCoreInstrumentation` 的 options 对象配置）。
5. **`UsePrometheusScrapingEndpoint` 实际方法名是 `UseOpenTelemetryPrometheusScrapingEndpoint`**：位于 `OpenTelemetry` 命名空间（`OpenTelemetry.ServiceCollectionExtensions`），不是 `OpenTelemetry.Exporter.Prometheus` 子命名空间。API Program.cs 已用正确方法名。

---

## 3. 文件变更清单

### 新增

```text
src/HospitalAi.Domain/Security/AppUser.cs
src/HospitalAi.Domain/Security/AppRole.cs
src/HospitalAi.Domain/Security/AppUserRole.cs
src/HospitalAi.Application/Security/ISecurityService.cs
src/HospitalAi.Infrastructure/Security/SqlServerSecurityService.cs
src/HospitalAi.Contracts/Security/SecurityContracts.cs
src/HospitalAi.Infrastructure/Observability/ObservabilityServiceCollectionExtensions.cs
src/HospitalAi.Worker/Observability/PipelineMetrics.cs
tests/HospitalAi.Domain.Tests/RbacTests.cs
tests/HospitalAi.Application.Tests/RbacServiceTests.cs
src/HospitalAi.Infrastructure/SqlServer/Migrations/20260918170609_RbacTables.cs
src/HospitalAi.Infrastructure/SqlServer/Migrations/20260918170609_RbacTables.Designer.cs
docs/superpowers/reports/2026-09-18-phase1-rbac-otel-supplement.md
```

### 修改

```text
src/HospitalAi.Infrastructure/SqlServer/PersistenceRecords.cs       (AppUserRecord/AppRoleRecord/AppUserRoleRecord)
src/HospitalAi.Infrastructure/SqlServer/HospitalAiDbContext.cs       (三表映射 + 唯一索引 + FK)
src/HospitalAi.Api/Program.cs                                        (RBAC 端点 + OTel 注册 + /metrics)
src/HospitalAi.Worker/Program.cs                                     (Worker OTel 注册)
src/HospitalAi.Worker/RabbitMqWorkerRegistration.cs                 (移除 UseInstrumentation)
src/HospitalAi.Worker/Consumers/CodingTaskCreatedConsumer.cs        (集成 PipelineMetrics)
src/HospitalAi.Worker/Worker.cs                                      (删除死代码 BackgroundService)
src/HospitalAi.Worker/Pipeline/SqlServerCodingRecommendationPipelineRunner.cs  (注释)
src/HospitalAi.Infrastructure/HospitalAi.Infrastructure.csproj       (OTel 包引用)
src/HospitalAi.Api/HospitalAi.Api.csproj                             (OTel 包引用)
src/HospitalAi.Worker/HospitalAi.Worker.csproj                       (OTel 包引用)
docs/superpowers/specs/2026-09-15-phase1-platform-foundation-design.md   (.NET 10 + medical_document_version 同步 + 验收标准)
docs/superpowers/plans/2026-09-15-phase1-platform-foundation-plan.md    (.NET 10 + Task 10-14)
docs/superpowers/Phase1_智能编码推荐_本地模型化部署_开发实施基线_V2.2.md  (.NET 10)
README.md                                                            (RBAC 端点 + /metrics + 已知未交付项)
.env                                                                   (密码统一为 HospitalAi2026)
```

---

## 4. 后续步骤

1. ~~**在有网、可用 CMD 的 Windows 环境执行完整集成测试**（设置 `HOSPITAL_AI_TEST_CONNECTION_STRING` 后 `dotnet test`）。~~ ✅ 已在本机通过独立 `docker run` SQL Server 容器完成（见 §2.4）。
2. **运行冒烟脚本**（`pwsh -File tests/smoke/run.ps1`）验证 Docker Compose 全栈闭环（API/Worker 容器 + SQL Server + Redis + RabbitMQ）。
3. **正式权限矩阵**：`app_role` 目前只有最小模型，角色-资源-动作矩阵待医院接入前确认（设计文档"后续门禁"）。
4. **Worker Prometheus 端点**：如需 Worker 侧 HTTP 指标端点，可考虑将 Worker 改为 `Microsoft.NET.Sdk.Web` + 最小 `WebApplication`，或通过 OTLP collector 中转。
5. **OTel `Filter` 配置**：后续可通过 `AspNetCoreMetricsOptions.Filter` 过滤健康检查端点（1.10.0 需确认 API 变更）。
