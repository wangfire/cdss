# -*- coding: utf-8 -*-
"""迁移补种子行（v22lite 默认关闭字典标题直达）。"""
import io
import re
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

import glob

f = glob.glob(r"src\HospitalAi.Infrastructure\SqlServer\Migrations\*PipelineRuntimeConfig.cs")[0]
c = open(f, encoding="utf-8").read()

if "InsertData" in c:
    print("seed already present")
    sys.exit(0)

seed_json = '{"IcdTitleDirectEnabled":false,"Cm3TitleDirectEnabled":false}'
escaped = seed_json.replace('"', '\\"')

seed = (
    "\n"
    "            migrationBuilder.InsertData(\n"
    '                table: "pipeline_runtime_config",\n'
    '                columns: new[] { "id", "config_key", "config_json", "updated_at" },\n'
    "                values: new object[]\n"
    "                {\n"
    '                    new Guid("5A1F7C2E-3B4D-4E5F-8A9B-0C1D2E3F4A5B"),\n'
    '                    "v22lite",\n'
    f'                    "{escaped}",\n'
    "                    new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero)\n"
    "                });\n"
)

m = re.search(r"(migrationBuilder\.CreateIndex\([^;]+\);)", c, re.DOTALL)
assert m, "create index anchor"
c = c.replace(m.group(1), m.group(1) + seed, 1)
open(f, "w", encoding="utf-8").write(c)
print("seed added")
