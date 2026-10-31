# -*- coding: utf-8 -*-
"""i18n key 追加：knowledge.indexEntryDisabled / Enabled / disableIndexEntry / enableIndexEntry。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

files = [
    (
        r"web-ui\apps\admin\src\i18n\locales\zh-CN\knowledge.ts",
        "索引行已删除",
        {"indexEntryDisabled": "索引行已停用", "indexEntryEnabled": "索引行已启用",
         "disableIndexEntry": "停用", "enableIndexEntry": "启用"},
    ),
    (
        r"web-ui\apps\admin\src\i18n\locales\en-US\knowledge.ts",
        "Index entry deleted",
        {"indexEntryDisabled": "Index entry disabled", "indexEntryEnabled": "Index entry enabled",
         "disableIndexEntry": "Disable", "enableIndexEntry": "Enable"},
    ),
]

for path, anchor_value, keys in files:
    c = open(path, encoding="utf-8").read()
    anchor = f"  indexEntryDeleted: '{anchor_value}',"
    assert anchor in c, path
    additions = "".join(f"  {k}: '{v}',\n" for k, v in keys.items())
    c = c.replace(anchor, anchor + "\n" + additions.rstrip("\n"), 1)
    open(path, "w", encoding="utf-8").write(c)
    print(path, "keys added")
