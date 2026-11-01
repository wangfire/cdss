# -*- coding: utf-8 -*-
"""WP-D i18n 收口：分类硬编码清单。

结论分类：
  [示例数据/占位] Knowledge 1225-1236（同义词/规则示例 mock 数据）、2342/2360（JSON placeholder 示例）——内部演示数据，不面向用户
  [管道阶段映射] Workbench 273-290（步骤名/降级标记 → 中文标签映射表）——需 i18n
  [主题切换] ThemeSwitcher 15-17/33/57/73/77 —— 需 i18n
  [无障碍 aria-label] DashboardLayout 507/619/918 —— 需 i18n
  [混合模板] CaseEntry 361 —— "第 X 次住院" 需 i18n
"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
