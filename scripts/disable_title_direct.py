# -*- coding: utf-8 -*-
"""关闭字典标题直达（ICD + CM3）——索引匹配口径。

修改 Worker appsettings.json 的 V22Lite 节：
  IcdTitleDirectEnabled: true → false（诊断侧字典标题直达关闭）
  Cm3TitleDirectEnabled: true → false（手术侧字典标题直达关闭）
保留 IcdTitleReverseEnabled（同族标题反查是索引链路的回退，不是字典直达）。
"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

for p in (r"src\HospitalAi.Worker\appsettings.json", r"src\HospitalAi.Api\appsettings.json"):
    c = open(p, encoding="utf-8").read()
    changed = False
    for key in ("IcdTitleDirectEnabled", "Cm3TitleDirectEnabled"):
        old_true = f'"{key}": true'
        if old_true in c:
            c = c.replace(old_true, f'"{key}": false')
            changed = True
            print(f"{p}: {key} → false")
    if changed:
        open(p, "w", encoding="utf-8").write(c)
    else:
        print(f"{p}: no TitleDirect keys (maybe absent)")
