# -*- coding: utf-8 -*-
"""WP1 后端：流水线运行时配置表（单行 key/value JSON）。

文件改动：
  1. PersistenceRecordsV22.cs 追加 PipelineRuntimeConfigRecord 实体
  2. HospitalAiDbContext.cs 加 DbSet + OnModelCreating 配置
  3. 新增迁移（手写迁移文件对齐现有迁移命名）
  4. Worker：V22LiteCodingPipelineRunner.StartAsync 里 DB 读覆盖项合并 IOptions
"""
import io
import re
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

# ---- 1. 实体 ----
p_rec = r"src\HospitalAi.Infrastructure\SqlServer\PersistenceRecordsV22.cs"
c = open(p_rec, encoding="utf-8").read()
if "PipelineRuntimeConfigRecord" not in c:
    addition = """

/// <summary>
/// 流水线运行时配置（WP-A 前端生产化：工作台设置抽屉）。
/// 单行键值：config_key='v22lite'，config_json 为 V22PipelineOptions 的覆盖项 JSON。
/// API 更新后 Worker 在下一次任务执行时读取生效（无缓存，任务级新鲜度）。
/// </summary>
public sealed class PipelineRuntimeConfigRecord
{
    public Guid Id { get; set; }

    public string ConfigKey { get; set; } = string.Empty;

    public string ConfigJson { get; set; } = "{}";

    public DateTimeOffset UpdatedAt { get; set; }
}
"""
    c = c.rstrip("\n") + "\n" + addition
    open(p_rec, "w", encoding="utf-8").write(c)
    print("entity added")

# ---- 2. DbSet ----
p_ctx = r"src\HospitalAi.Infrastructure\SqlServer\HospitalAiDbContext.cs"
c2 = open(p_ctx, encoding="utf-8").read()
if "DbSet<PipelineRuntimeConfigRecord>" not in c2:
    anchor = "    public DbSet<ClinicalFactRecord> ClinicalFacts => Set<ClinicalFactRecord>();"
    assert anchor in c2, "dbset anchor"
    c2 = c2.replace(
        anchor,
        anchor + "\n\n    public DbSet<PipelineRuntimeConfigRecord> PipelineRuntimeConfigs => Set<PipelineRuntimeConfigRecord>();",
        1,
    )
    open(p_ctx, "w", encoding="utf-8").write(c2)
    print("DbSet added")

# ---- 3. OnModelCreating 配置（找 ClinicalFact 的映射段样式）----
m = re.search(r"modelBuilder\.Entity<ClinicalFactRecord>\(entity =>\s*{", c2)
print("ClinicalFact mapping found:", bool(m))
