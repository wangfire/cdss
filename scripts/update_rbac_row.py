# -*- coding: utf-8 -*-
"""总汇报 docx：RBAC 行尚缺部分更新（三项已完成）。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

p = r"scripts\build_project_report_docx.py"
c = open(p, encoding="utf-8").read()

old = "生产 JWT Key 密钥管理注入、审计日志补记解锁动作、登录页锁定倒计时提示（后端已带数据）"
new = "（2026-10-31 已全部补齐 ✅）生产 JWT Key 密钥管理注入（secrets.env → prod 编排 JwtAuth__Key → 启动强制校验）；登录失败/锁定触发审计留痕（app_user.login_failed / lockout_triggered）；登录页 423 倒计时提示（前端已实现，后端带剩余分钟数）"
assert old in c, "rbac尚缺 anchor"
c = c.replace(old, new, 1)

open(p, "w", encoding="utf-8").write(c)
print("rbac row updated")
