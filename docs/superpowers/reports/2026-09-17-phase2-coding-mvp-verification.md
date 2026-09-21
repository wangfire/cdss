# Phase 2 智能编码推荐 MVP 验证报告

## 范围

- 知识库：ICD-10、ICD-9-CM-3 编码导入，同义词和规则导入。
- 数据模型：编码体系、医学编码、同义词、规则、文书段落、临床实体、推荐、证据、审核、最终编码。
- Pipeline：文书读取、段落化、实体识别基线、候选召回、置信度、推荐和证据落库。
- API：字典导入、规则导入、推荐查询、人工审核、工作台任务列表。
- Worker：RabbitMQ 消费后生成推荐，任务进入 `PENDING_REVIEW` 或 `HUMAN_REQUIRED`。
- 审核闭环：人工确认后写入 `final_coding_result`。

## 验证命令

```powershell
dotnet format HospitalAi.slnx --verify-no-changes --no-restore
$env:HOSPITAL_AI_TEST_CONNECTION_STRING='Server=localhost,1433;Database=HospitalAiTests;User Id=sa;Password=<local-password>;Encrypt=False;TrustServerCertificate=True;'
dotnet test HospitalAi.slnx --configuration Release --no-restore
$env:HOSPITAL_AI_CONNECTION_STRING='Server=localhost,1433;Database=HospitalAi;User Id=sa;Password=<local-password>;Encrypt=False;TrustServerCertificate=True;'
dotnet ef database update --project src/HospitalAi.Infrastructure --startup-project src/HospitalAi.Api --context HospitalAiDbContext
pwsh -File tests/smoke/run.ps1
```

## 结果

- 格式检查：通过。
- 全量测试：通过，47/47。
  - Domain：10/10。
  - Application：11/11。
  - Infrastructure：13/13。
  - API：7/7。
  - Worker：6/6。
- 数据库迁移：`Phase2CodingMvp` 已应用，本地 `HospitalAi` 数据库为最新状态。
- Docker 冒烟：通过。
  - 任务 ID：`33e8d62c-c1a9-4f38-b508-6ef5f98b767c`。
  - Trace ID：`bacd58b8f7964c37b4838e14ff9e9376`。
  - 任务进入 `PENDING_REVIEW`，人工审核返回 `ACCEPTED`。

## 注意事项

- Docker SQL Server 使用已有数据卷时，`HOSPITAL_AI_SQL_PASSWORD` 必须与该卷首次初始化密码一致；本机本次使用历史开发密码后通过。
- 本阶段推荐算法仍是规则和 SQL 字典基线，不承诺生产准确率。
- 样例数据位于 `data/phase2/`，均为脱敏/模拟数据，不包含真实病历。

## 2026-09-18 续验

- Phase 2 定向测试：9/9 通过。
- 完整测试：52/52 通过。
  - Domain：10/10。
  - Application：16/16。
  - Infrastructure：13/13。
  - API：7/7。
  - Worker：6/6。
- `git diff --check`：通过，仅有 Windows 换行符提示。
- API `GET /api/v1/health`：200，SQL Server 连接正常。
- API `GET /api/v1/health/ready`：503；SQL Server healthy，Redis 和 RabbitMQ 未启动，因此整体 readiness 为 unhealthy。
- 本次修复测试辅助方法对 SQL Server 兼容级别使用白名单固定 SQL，避免生成不支持的参数化 `ALTER DATABASE` 语句。

## 2026-09-18 知识库续验

- Python 知识库构建测试：2/2 通过。
- 知识库脚本自检：通过。
- ICD-10 编码：36,662 条，其中启用 36,007 条。
- ICD-9-CM-3 编码：14,388 条，其中启用 14,349 条。
- ICD-10 索引同义词：6,134 条；仅保留 `NEC状态=0`。
- 深圳医保规则：2,178 条。
- 组合编码 `E74.0† I43.1*` 已生成并保持为完整编码 `E74.0+I43.1*`，未拆分为两个编码。
- `13三体综合征` 和 `帕套综合征` 均映射到 `Q91.7`。
- 生成产物位于 `data/knowledge/`，原始 Excel 未修改。
