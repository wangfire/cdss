# -*- coding: utf-8 -*-
"""Knowledge.vue：在六处删除按钮后插入停用/启用按钮（按 kind 定制）。"""
import io
import re
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

p = r"web-ui\apps\admin\src\views\Knowledge.vue"
c = open(p, encoding="utf-8").read()

anchor = "async function deleteIndexEntry(kind: IndexEditKind, row: IndexEditRow) {"
toggle_fn = """async function toggleIndexEntryEnabled(kind: IndexEditKind, row: IndexEditRow, enabled: boolean) {
  try {
    await knowledgeApi.setIndexEnabled(kind, row.id, enabled)
    toast.success(t(enabled ? 'knowledge.indexEntryEnabled' : 'knowledge.indexEntryDisabled'))
    await refreshIndexLevel(kind, row.parentSeqNo)
  } catch (error) {
    toast.error(error instanceof ApiError ? error.message : t('knowledge.deleteFailed'))
  }
}

""" + anchor
if "toggleIndexEntryEnabled" not in c:
    c = c.replace(anchor, toggle_fn, 1)

specs = [
    ("icd", "row", "canDeleteIcd"),
    ("icd", "row.node", "canDeleteIcd"),
    ("cm3", "row", "canDeleteCm3"),
    ("cm3", "row.node", "canDeleteCm3"),
    ("tumor", "row", "canDeleteTumor"),
    ("tumor", "row.node", "canDeleteTumor"),
]
count = 0
for kind, var, perm in specs:
    call = f"deleteIndexEntry('{kind}', {var})"
    marker = f'@click="{call}">'
    idx = c.find(marker)
    if idx < 0:
        print(f"marker not found: {call}")
        continue
    # 找包含 marker 的整行结束位置（该 Button 是单行 + 后随两行 Trash2/</Button>）
    line_end = c.index("\n", idx)
    # 找该 Button 的 </Button> 结束
    btn_end = c.index("</Button>", line_end) + len("</Button>")
    indent = c[:idx].rsplit("\n", 1)[-1][: len(c[:idx].rsplit("\n", 1)[-1]) - len(c[:idx].rsplit("\n", 1)[-1].lstrip())]
    if var == "row":
        row_var = "row"
    else:
        row_var = "row.node"
    disable_btn = (
        f'\n{indent}<Button v-if="{perm} && row.isEnabled !== false'
        if var == "row"
        else f'\n{indent}<Button v-if="{perm} && row.node.isEnabled !== false'
    )
    # 直接构造两枚按钮：停用（当 isEnabled!==false）与 启用（当 isEnabled===false）
    if var == "row":
        cond_disable = f'{perm} && row.isEnabled !== false'
        cond_enable = f'{perm} && row.isEnabled === false'
    else:
        cond_disable = f'{perm} && row.node.isEnabled !== false'
        cond_enable = f'{perm} && row.node.isEnabled === false'
    rowvar = var
    buttons = (
        f'\n{indent}<Button v-if="{cond_disable}" variant="ghost" size="sm" class="h-7 w-7 p-0 text-muted-foreground" :title="t(\'knowledge.disableIndexEntry\')" @click="toggleIndexEntryEnabled(\'{kind}\', {rowvar}, false)">\n'
        f'{indent}  <CircleOff class="h-3.5 w-3.5" />\n'
        f'{indent}</Button>\n'
        f'{indent}<Button v-if="{cond_enable}" variant="ghost" size="sm" class="h-7 w-7 p-0 text-emerald-600" :title="t(\'knowledge.enableIndexEntry\')" @click="toggleIndexEntryEnabled(\'{kind}\', {rowvar}, true)">\n'
        f'{indent}  <CheckCircle2 class="h-3.5 w-3.5" />\n'
        f'{indent}</Button>'
    )
    c = c[:btn_end] + buttons + c[btn_end:]
    count += 1

open(p, "w", encoding="utf-8").write(c)
print(f"buttons inserted: {count}")
