# -*- coding: utf-8 -*-
"""把 PipelineRuntimeConfigRecord 加进 ModelSnapshot（手写，机械对齐现有格式）。

插入位置：最后一个实体（VisitRecord）导航属性块之前，与其他实体块并列。
"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

snap = r"src\HospitalAi.Infrastructure\SqlServer\Migrations\HospitalAiDbContextModelSnapshot.cs"
c = open(snap, encoding="utf-8").read()

if "PipelineRuntimeConfigRecord" in c:
    print("already in snapshot")
    sys.exit(0)

block = """            modelBuilder.Entity("HospitalAi.Infrastructure.SqlServer.PipelineRuntimeConfigRecord", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uniqueidentifier")
                        .HasColumnName("id");

                    b.Property<string>("ConfigJson")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)")
                        .HasColumnName("config_json");

                    b.Property<string>("ConfigKey")
                        .IsRequired()
                        .HasMaxLength(64)
                        .HasColumnType("nvarchar(64)")
                        .HasColumnName("config_key");

                    b.Property<DateTimeOffset>("UpdatedAt")
                        .HasColumnType("datetimeoffset(7)")
                        .HasColumnName("updated_at");

                    b.HasKey("Id");

                    b.HasIndex("ConfigKey")
                        .IsUnique();

                    b.ToTable("pipeline_runtime_config", (string)null);
                });

"""

anchor = '            modelBuilder.Entity("HospitalAi.Infrastructure.SqlServer.VisitRecord", b =>\n                {\n                    b.Navigation("CodingTasks");\n                });'
assert anchor in c, "nav anchor"
c = c.replace(anchor, block + anchor, 1)
open(snap, "w", encoding="utf-8").write(c)
print("snapshot updated")
