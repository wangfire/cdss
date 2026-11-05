# -*- coding: utf-8 -*-
"""修正快照：属性按字母序（EF 生成快照的规范顺序）ConfigJson → ConfigKey → Id → UpdatedAt。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

snap = r"src\HospitalAi.Infrastructure\SqlServer\Migrations\HospitalAiDbContextModelSnapshot.cs"
c = open(snap, encoding="utf-8").read()

old = """            modelBuilder.Entity("HospitalAi.Infrastructure.SqlServer.PipelineRuntimeConfigRecord", b =>
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
                });"""

new = """            modelBuilder.Entity("HospitalAi.Infrastructure.SqlServer.PipelineRuntimeConfigRecord", b =>
                {
                    b.Property<string>("ConfigJson")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)")
                        .HasColumnName("config_json");

                    b.Property<string>("ConfigKey")
                        .IsRequired()
                        .HasMaxLength(64)
                        .HasColumnType("nvarchar(64)")
                        .HasColumnName("config_key");

                    b.Property<Guid>("Id")
                        .HasColumnType("uniqueidentifier")
                        .HasColumnName("id");

                    b.Property<DateTimeOffset>("UpdatedAt")
                        .HasColumnType("datetimeoffset(7)")
                        .HasColumnName("updated_at");

                    b.HasKey("Id");

                    b.HasIndex("ConfigKey")
                        .IsUnique();

                    b.ToTable("pipeline_runtime_config", (string)null);
                });"""

assert old in c, "snapshot block"
c = c.replace(old, new, 1)
open(snap, "w", encoding="utf-8").write(c)
print("snapshot property order fixed")
