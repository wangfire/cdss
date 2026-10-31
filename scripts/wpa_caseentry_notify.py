# -*- coding: utf-8 -*-
"""CaseEntry.vue：管道任务完成时推送本地会话通知（WP-C）。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

p = r"web-ui\apps\admin\src\views\CaseEntry.vue"
c = open(p, encoding="utf-8").read()

# 1. 引入 notification store（紧跟 user store 或任一 store import 之后）
import re
m = re.search(r"import \{ use\w+Store \} from '@/stores/\w+'", c)
if m and "useNotificationStore" not in c:
    insert_after = m.group(0)
    c = c.replace(
        insert_after,
        insert_after + "\nimport { useNotificationStore } from '@/stores/notification'",
        1,
    )
    # 在 const xxxStore = use...() 之后实例化
    m2 = re.search(r"const (\w+Store) = use\w+Store\(\)", c)
    if m2:
        c = c.replace(
            m2.group(0),
            m2.group(0) + "\nconst notificationStore = useNotificationStore()",
            1,
        )
    print("notification store imported")

# 2. 任务完成分支推送通知
old_done = """      } else {
        toast.success(t('caseEntry.successTitle'))
      }"""
new_done = """      } else {
        toast.success(t('caseEntry.successTitle'))
        // WP-C：批量管道任务完成 → 本地会话通知（含耗时）
        const elapsedSec = Math.round((Date.now() - submitStartAt) / 1000)
        notificationStore.addNotification({
          title: t('caseEntry.notifTaskDoneTitle'),
          message: t('caseEntry.notifTaskDoneMsg', { patient: form.patientName, seconds: elapsedSec }),
          type: 'success',
          actionUrl: `/workbench?task=${resp.codingTaskId}`,
          actionLabel: t('caseEntry.notifTaskView'),
        })
      }"""
assert old_done in c, "done branch anchor"
c = c.replace(old_done, new_done, 1)

# 3. 失败/超时分支也推送
old_fail = """      if (outcome === 'failed') {
        toast.error(t('caseEntry.taskFailed'))
      } else if (outcome === 'timeout') {
        toast.error(t('caseEntry.taskTimeout'))
      }"""
new_fail = """      if (outcome === 'failed') {
        toast.error(t('caseEntry.taskFailed'))
        notificationStore.addNotification({
          title: t('caseEntry.notifTaskFailTitle'),
          message: t('caseEntry.notifTaskFailMsg', { patient: form.patientName }),
          type: 'error',
        })
      } else if (outcome === 'timeout') {
        toast.error(t('caseEntry.taskTimeout'))
        notificationStore.addNotification({
          title: t('caseEntry.notifTaskTimeoutTitle'),
          message: t('caseEntry.notifTaskTimeoutMsg', { patient: form.patientName }),
          type: 'warning',
        })
      }"""
assert old_fail in c, "fail branch anchor"
c = c.replace(old_fail, new_fail, 1)

# 4. 提交起始时间戳（耗时计算）
anchor_submit = "    result.value = resp"
c = c.replace(anchor_submit, "    const submitStartAt = Date.now()\n" + anchor_submit, 1)

open(p, "w", encoding="utf-8").write(c)
print("CaseEntry.vue notifications wired")
