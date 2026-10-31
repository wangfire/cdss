# -*- coding: utf-8 -*-
"""i18n：login.ts 补医院下拉 key（zh/en），锚点用 hospitalIdInvalid 实际文本。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

files = [
    (
        r"web-ui\apps\admin\src\i18n\locales\zh-CN\login.ts",
        "  hospitalIdInvalid: '医院 ID 必须是合法的 GUID',",
        {
            "hospitalSelectPlaceholder": "请选择医院",
            "hospitalManualInput": "手动输入医院 ID…",
        },
    ),
    (
        r"web-ui\apps\admin\src\i18n\locales\en-US\login.ts",
        "  hospitalIdInvalid: 'Hospital ID must be a valid GUID',",
        {
            "hospitalSelectPlaceholder": "Select a hospital",
            "hospitalManualInput": "Enter hospital ID manually…",
        },
    ),
]

for path, anchor, keys in files:
    c = open(path, encoding="utf-8").read()
    if "hospitalSelectPlaceholder" in c:
        print(path, "already present")
        continue
    assert anchor in c, path
    additions = "".join(f"  {k}: '{v}',\n" for k, v in keys.items())
    c = c.replace(anchor, anchor + "\n" + additions.rstrip("\n"), 1)
    open(path, "w", encoding="utf-8").write(c)
    print(path, "keys added")
