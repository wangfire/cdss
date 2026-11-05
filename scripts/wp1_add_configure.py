# -*- coding: utf-8 -*-
"""DbContext：加 ConfigurePipelineRuntimeConfig 方法 + 调用。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

p = r"src\HospitalAi.Infrastructure\SqlServer\HospitalAiDbContext.cs"
c = open(p, encoding="utf-8").read()

# 1. OnModelCreating 调用
old_call = "        ConfigureClinicalFact(modelBuilder);\n        ConfigureClinicalEvidence(modelBuilder);"
new_call = (
    "        ConfigureClinicalFact(modelBuilder);\n"
    "        ConfigureClinicalEvidence(modelBuilder);\n"
    "        ConfigurePipelineRuntimeConfig(modelBuilder);"
)
if "ConfigurePipelineRuntimeConfig(modelBuilder)" not in c:
    assert old_call in c, "call anchor"
    c = c.replace(old_call, new_call, 1)

# 2. 方法定义（放在 ConfigureClinicalFact 方法结束后——找该方法结尾：private static void ConfigureClinicalEvidence 之前）
method = """    private static void ConfigurePipelineRuntimeConfig(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PipelineRuntimeConfigRecord>();

        entity.ToTable("pipeline_runtime_config");

        entity.Property(item => item.Id).HasColumnName("id");
        entity.Property(item => item.ConfigKey).HasColumnName("config_key").HasMaxLength(64).IsRequired();
        entity.Property(item => item.ConfigJson).HasColumnName("config_json").IsRequired();
        entity.Property(item => item.UpdatedAt).HasColumnName("updated_at");
        entity.HasIndex(item => item.ConfigKey).IsUnique();
    }

"""

anchor = "    private static void ConfigureClinicalEvidence(ModelBuilder modelBuilder)"
assert anchor in c, "method anchor"
c = c.replace(anchor, method + anchor, 1)
open(p, "w", encoding="utf-8").write(c)
print("configure method added")
