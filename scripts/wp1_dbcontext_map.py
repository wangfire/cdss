# -*- coding: utf-8 -*-
"""DbContext 加 pipeline_runtime_config 的 snake_case 映射 + 删除 EF 纠正迁移 + 重新生成。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

p = r"src\HospitalAi.Infrastructure\SqlServer\HospitalAiDbContext.cs"
c = open(p, encoding="utf-8").read()

if 'entity.ToTable("pipeline_runtime_config")' in c:
    print("mapping already present")
else:
    # 找 ClinicalFacts 的 ToTable 行作锚
    import re
    m = re.search(r'( +)entity\.ToTable\("clinical_fact"\);', c)
    assert m, "clinical_fact anchor"
    insertion = (
        m.group(0)
        + f"\n\n{m.group(1)}entity.ToTable(\"pipeline_runtime_config\");"
        + f"\n{m.group(1)}entity.Property(item => item.Id).HasColumnName(\"id\");"
        + f"\n{m.group(1)}entity.Property(item => item.ConfigKey).HasColumnName(\"config_key\").HasMaxLength(64).IsRequired();"
        + f"\n{m.group(1)}entity.Property(item => item.ConfigJson).HasColumnName(\"config_json\").IsRequired();"
        + f"\n{m.group(1)}entity.Property(item => item.UpdatedAt).HasColumnName(\"updated_at\");"
        + f"\n{m.group(1)}entity.HasIndex(item => item.ConfigKey).IsUnique();"
    )
    c = c.replace(m.group(0), insertion, 1)
    open(p, "w", encoding="utf-8").write(c)
    print("mapping added")

# 删 EF 生成的纠正迁移
import glob
for f in glob.glob(r"src\HospitalAi.Infrastructure\SqlServer\Migrations\*PipelineRuntimeConfig*"):
    import os
    os.remove(f)
    print("removed:", os.path.basename(f))

# 同时恢复快照：重新生成（EF 会基于正确映射重建快照——直接删快照块让 ef add 重写？不行，快照必须有效。
# 现在 DbContext 有了显式映射，EF add 会生成 CreateTable(pipeline_runtime_config) + 更新快照）
