# -*- coding: utf-8 -*-
"""i18n：caseEntry.ts 补管道任务通知 key（zh/en）。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

files = [
    (
        r"web-ui\apps\admin\src\i18n\locales\zh-CN\caseEntry.ts",
        {
            "notifTaskDoneTitle": "编码推荐完成",
            "notifTaskDoneMsg": "患者「{patient}」的编码推荐已完成，耗时 {seconds} 秒",
            "notifTaskView": "查看任务",
            "notifTaskFailTitle": "编码任务失败",
            "notifTaskFailMsg": "患者「{patient}」的编码任务执行失败，请重试或联系管理员",
            "notifTaskTimeoutTitle": "编码任务超时",
            "notifTaskTimeoutMsg": "患者「{patient}」的编码任务超时未完成，请稍后在任务列表查看",
        },
    ),
    (
        r"web-ui\apps\admin\src\i18n\locales\en-US\caseEntry.ts",
        {
            "notifTaskDoneTitle": "Coding recommendation completed",
            "notifTaskDoneMsg": "Coding recommendation for \"{patient}\" completed in {seconds}s",
            "notifTaskView": "View task",
            "notifTaskFailTitle": "Coding task failed",
            "notifTaskFailMsg": "Coding task for \"{patient}\" failed, please retry or contact admin",
            "notifTaskTimeoutTitle": "Coding task timed out",
            "notifTaskTimeoutMsg": "Coding task for \"{patient}\" timed out, check the task list later",
        },
    ),
]

for path, keys in files:
    c = open(path, encoding="utf-8").read()
    if "notifTaskDoneTitle" in c:
        print(path, "already present")
        continue
    # 锚点：taskTimeout 或 successTitle 附近任意已有 key；用文件最后一个 key 后追加
    # 找 "  xxx: 'yyy'," 的最后一行，在其后追加
    lines = c.rstrip("\n").split("\n")
    # 找闭合 "}" 前最后一个键行
    close_idx = next(i for i in range(len(lines) - 1, -1, -1) if lines[i].strip() == "}")
    additions = "\n".join(f"  {k}: '{v}'," for k, v in keys.items())
    lines.insert(close_idx, additions)
    open(path, "w", encoding="utf-8").write("\n".join(lines))
    print(path, "keys added")
