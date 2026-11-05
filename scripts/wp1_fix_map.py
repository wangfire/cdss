# -*- coding: utf-8 -*-
"""删除错误插入 ClinicalFactRecord 实体里的 pipeline_runtime_config 映射块。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

p = r"src\HospitalAi.Infrastructure\SqlServer\HospitalAiDbContext.cs"
c = open(p, encoding="utf-8").read()

bad = (
    '        entity.ToTable("pipeline_runtime_config");\n'
    '        entity.Property(item => item.Id).HasColumnName("id");\n'
    '        entity.Property(item => item.ConfigKey).HasColumnName("config_key").HasMaxLength(64).IsRequired();\n'
    '        entity.Property(item => item.ConfigJson).HasColumnName("config_json").IsRequired();\n'
    '        entity.Property(item => item.UpdatedAt).HasColumnName("updated_at");\n'
    '        entity.HasIndex(item => item.ConfigKey).IsUnique();\n'
)
assert bad in c, "bad block"
c = c.replace(bad, "", 1)
open(p, "w", encoding="utf-8").write(c)
print("bad block removed")
