# -*- coding: utf-8 -*-
"""8.5 表追加两行复核（RBAC 尚缺补齐 + i18n 静态文案收口）。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

p = r"scripts\build_project_report_docx.py"
c = open(p, encoding="utf-8").read()

anchor_row = '''        ["生产部署四包", "prod 编排/nginx conf/deploy.ps1/secrets 模板/DataProtection 卷全部在位；构建 0 错误", "✅ 编码完成（真实环境执行待验证）"],'''
assert anchor_row in c, "ch8 table anchor"

new_rows = anchor_row + '''
        ["RBAC 尚缺三项（JWT 注入/锁定审计/倒计时）", "prod compose JwtAuth__Key 注入链路 + secrets 模板在位；SqlServerAuthService 写 app_user.login_failed / lockout_triggered 审计；Login.vue 423 倒计时完整链路；测试断言（失败 ≥5 + LOCKED + FAILED）14/14 绿", "✅ 已解决（10-31 补齐）"],
        ["前端 i18n 静态文案收口", "管道阶段/降级标记映射 15 key、主题切换 5 key（新建 theme.ts）、aria-label（新建 layout.ts）、第X次住院；复扫剩余 10 行全部为知识库示例数据（领域数据非 UI 文案）", "✅ 已解决（10-31，5f1a98c）"],'''
c = c.replace(anchor_row, new_rows, 1)

open(p, "w", encoding="utf-8").write(c)
print("8.5 table extended with 2 rows")
