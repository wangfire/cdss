# HospitalAi Phase 2 智能编码推荐 MVP

## 开发冒烟环境

启动：

```powershell
Copy-Item .env.example .env
# 编辑 .env，设置本机开发用的强密码
docker compose -f deploy/docker-compose.dev.yml up --build -d
```

API 地址：

```text
http://localhost:5080
```

就绪检查：

```text
GET http://localhost:5080/api/v1/health/ready
```

RabbitMQ 管理台：

```text
http://localhost:15673
```

开发凭据见 `.env.example`，首次启动前请按需修改。SQL Server 对宿主机暴露 `14333`，避免和本机 `1433` 冲突。

运行冒烟脚本：

```powershell
pwsh -File tests/smoke/run.ps1
```

如本机 Docker Compose 的 BuildKit 通道异常，脚本会自动回退到传统
`docker build` 构建 API/Worker 镜像后继续启动。若已经提前构建过镜像，也可显式跳过构建：

```powershell
pwsh -File tests/smoke/run.ps1 -SkipBuild
```

停止：

```powershell
docker compose -f deploy/docker-compose.dev.yml down
```

清理卷：

```powershell
docker compose -f deploy/docker-compose.dev.yml down -v
```

## 当前边界

Phase 2 已在 Phase 1 平台底座上实现智能编码推荐 MVP：支持 ICD-10 / ICD-9-CM-3 字典导入、脱敏文书段落化、规则和 SQL 字典基线推荐、证据链、人工审核和最终编码落库。

本阶段仍不接入真实医院接口、OCR、向量库、Reranker 和本地大模型；推荐算法是可解释基线，不承诺生产准确率。

### 已交付的平台能力（2026-09-18 补齐）

- **RBAC 最小模型**：`app_user` / `app_role` / `app_user_role` 三张表落地，提供用户身份、角色定义和用户-角色分配能力。端点：
  - `POST /api/v1/users`（按医院隔离，同 `code` 幂等）
  - `POST /api/v1/roles`（同 `code` 幂等）
  - `POST /api/v1/users/{userId}/roles/{roleId}`（分配角色，重复分配幂等）
  - `GET /api/v1/users/{userId}/roles`（查询用户角色）
  - `PATCH /api/v1/users/{userId}/status`（停用用户）
- **OpenTelemetry 指标**：API 暴露 HTTP/SQL/运行时指标，Worker 暴露 SQL/MassTransit 消费/流水线执行指标。
  - API Prometheus 端点：`http://localhost:5080/metrics`
  - 生产环境可替换为 OTLP 导出器。
- **编码规则预留**：`coding_rule` 表已入库并支持导入，规则引擎消费在后续 Phase。
- **死代码清理**：原 Phase 1 Task 7 之前的脚手架 `Worker` BackgroundService 已删除。

### 已知未交付项

- 正式权限矩阵、租户管理后台和生产级身份认证尚未实现（设计文档"后续门禁"）。
- Elasticsearch 检索、OCR、RAG、向量库、本地大模型未接入（设计文档"非目标"）。
- Worker 为 `Microsoft.NET.Sdk.Worker`（非 ASP.NET Core），其 Prometheus 端点未通过 `UsePrometheusScrapingEndpoint` 暴露 HTTP 路由；开发环境可通过 `dotnet-counters` 或 OTLP collector 抓取 Worker 指标。

## Phase 2 样例数据

```text
data/phase2/code-systems.sample.json
data/phase2/coding-rules.sample.json
data/phase2/golden-dataset.sample.json
```

审核工作台入口：

```text
http://localhost:5080/workbench
```

## 最终验证

完整交付前建议执行：

```powershell
dotnet format HospitalAi.slnx --verify-no-changes --no-restore
$env:HOSPITAL_AI_TEST_CONNECTION_STRING='Server=localhost,14333;Database=HospitalAiTests;User Id=sa;Password=<your-password>;TrustServerCertificate=True;Encrypt=False;'
dotnet test HospitalAi.slnx --configuration Release --no-restore
python -m unittest discover -s tests/knowledge -v
python tools/knowledge/build_knowledge.py --self-test
$env:HOSPITAL_AI_SQL_PASSWORD='<docker-sql-password>'
$env:HOSPITAL_AI_RABBITMQ_USER='guest'
$env:HOSPITAL_AI_RABBITMQ_PASSWORD='guest'
pwsh -File tests/smoke/run.ps1
```

Windows 主机连接 Docker 内 SQL Server 时，测试连接串需保留
`TrustServerCertificate=True;Encrypt=False;`，否则新版本 `Microsoft.Data.SqlClient`
可能因默认加密协商失败。

Phase 1 验证报告位于 `docs/superpowers/reports/2026-09-15-phase1-platform-foundation-verification.md`。
Phase 2 验证报告位于 `docs/superpowers/reports/2026-09-17-phase2-coding-mvp-verification.md`。
Phase 1 RBAC/OTel 补齐验证报告位于 `docs/superpowers/reports/2026-09-18-phase1-rbac-otel-supplement.md`。
