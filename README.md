# HospitalAi Phase 1 平台底座

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

Phase 1 只验证平台底座、Outbox/RabbitMQ/Worker 闭环、健康检查和基础审计日志。真实医院接口、AI 模型、OCR、RAG 和正式权限矩阵不在本阶段实现范围内。

## 最终验证

完整交付前建议执行：

```powershell
dotnet format HospitalAi.slnx --verify-no-changes --no-restore
$env:HOSPITAL_AI_TEST_CONNECTION_STRING='Server=localhost,14333;Database=HospitalAiTests;User Id=sa;Password=<your-password>;TrustServerCertificate=True;'
dotnet test HospitalAi.slnx --configuration Release --no-restore
pwsh -File tests/smoke/run.ps1
```

验证报告位于 `docs/superpowers/reports/2026-09-15-phase1-platform-foundation-verification.md`。
