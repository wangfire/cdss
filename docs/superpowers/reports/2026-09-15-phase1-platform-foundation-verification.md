# Phase 1 平台底座最终验证报告

## 范围

- API：请求上下文、异常处理、健康检查、医院/患者/就诊/文书/编码任务接口、Trace 查询。
- Application：编码任务创建、幂等键、跨医院隔离。
- Infrastructure：EF Core SQL Server 模型、迁移、索引、Outbox、Inbox、审计、Trace。
- Worker：RabbitMQ 消费、Inbox 去重、重试、Trace 步骤、任务状态流转。
- Compose：SQL Server 2019、Redis 7、RabbitMQ 3.13、API、Worker 开发冒烟环境。

## 环境

- OS：Windows，本地 Docker Desktop。
- .NET SDK：10.0.401。
- Docker：29.4.0。
- Docker Compose：5.1.1。
- 数据库：SQL Server 2019 容器，宿主机端口 `14333`。
- 消息队列：RabbitMQ 3.13 management 容器，宿主机端口 `5673` / `15673`。
- 缓存：Redis 7 alpine 容器，宿主机端口 `6380`。

## 验证命令

```powershell
dotnet format HospitalAi.slnx --verify-no-changes --no-restore
dotnet test HospitalAi.slnx --configuration Release --no-restore
pwsh -File tests/smoke/run.ps1
```

集成测试需要设置 `HOSPITAL_AI_TEST_CONNECTION_STRING`，示例：

```powershell
$env:HOSPITAL_AI_TEST_CONNECTION_STRING='Server=localhost,14333;Database=HospitalAiTests;User Id=sa;Password=<local-password>;TrustServerCertificate=True;'
```

## 结果

- `dotnet format HospitalAi.slnx --verify-no-changes --no-restore`：通过，退出码 0。
- `dotnet test HospitalAi.slnx --configuration Release`：通过，39/39。
  - Domain：9/9。
  - Application：5/5。
  - Infrastructure：13/13。
  - API：6/6。
  - Worker：6/6。
- `pwsh -File tests/smoke/run.ps1`：通过。
  - 默认 Compose BuildKit 构建首先触发本机 `x-docker-expose-session-sharedkey` gRPC 通道异常。
  - 冒烟脚本自动回退到传统 `docker build`，API/Worker 镜像构建成功。
  - 创建医院、患者、就诊和编码任务成功。
  - Worker 经 RabbitMQ 消费后任务进入 `SUCCESS`。
  - 本次任务 ID：`4be10804-9258-4f47-9442-773d221888bc`。
  - 本次 Trace ID：`654f4634cd1a41b59b41d4b3e5df6237`。
- Compose 服务状态：API、Worker、SQL Server、Redis、RabbitMQ 均为 healthy。

## 环境限制

- 当前阶段未接入真实医院 HIS/EMR/医保接口。
- 当前阶段未接入真实 AI 模型、OCR、RAG、向量库和 Prompt 工作流。
- 当前阶段只提供 RBAC 基础扩展点，未实现正式权限矩阵、租户管理后台和生产级身份认证。
- 开发凭据通过 `.env` 或环境变量提供，仓库不保存本地 SQL Server 密码。
- 本机 Docker Compose BuildKit 通道可能出现 `x-docker-expose-session-sharedkey` 请求头异常；冒烟脚本已提供传统 `docker build` 自动回退。

## 结论

Phase 1 平台底座已完成本地端到端验证：API 写入任务和 Outbox，RabbitMQ 投递消息，Worker 消费后完成占位编码流程，Trace 和审计链路可落库查询。
