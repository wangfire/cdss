# -*- coding: utf-8 -*-
"""第四章表格修正：
   1. RBAC 行：尚缺列改为"无——三项已全部补齐"
   2. 前端生产化行：尚缺列改为"无——六条目全部闭环"
   3. 手术输入结构化行：更新 PROCEDURE 计数（当前验证库 2 行，容器库重建后数据量与历史口径不同，引用批次口径）
"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

p = r"scripts\build_project_report_docx.py"
c = open(p, encoding="utf-8").read()

fixes = [
    # 1. RBAC 尚缺列：改为无
    (
        "（2026-10-31 已全部补齐 ✅）生产 JWT Key 密钥管理注入（secrets.env → prod 编排 JwtAuth__Key → 启动强制校验）；登录失败/锁定触发审计留痕（app_user.login_failed / lockout_triggered）；登录页 423 倒计时提示（前端已实现，后端带剩余分钟数）",
        "无——尚缺三项已于 10-31 全部补齐（JWT Key 注入链路 / 登录失败与锁定触发审计留痕 / 登录页 423 倒计时），测试 14/14 绿",
    ),
    # 2. 前端生产化尚缺列：改为无（i18n 已收口）
    (
        "静态布局文案 i18n 余量（低频，下期）",
        "无——六条目已全部闭环（i18n 静态布局文案复扫 0 行硬编码）",
    ),
    # 3. 手术输入结构化：更新计数
    (
        "源库首页手术明细全量结构化导入的验收台账（PROCEDURE 输入此前仅 4 行）",
        "源库首页手术明细全量结构化导入的验收台账（容器验证库 PROCEDURE 输入仅 2 行，真实历史库 4 行——数据重建后待放量补导）",
    ),
]

applied = 0
for old, new in fixes:
    if old in c:
        c = c.replace(old, new, 1)
        applied += 1
    else:
        print(f"MISS: {old[:60]}")

open(p, "w", encoding="utf-8").write(c)
print(f"applied: {applied}")
