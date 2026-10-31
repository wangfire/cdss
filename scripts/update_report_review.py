# -*- coding: utf-8 -*-
"""更新总汇报 docx：第四章状态表同步 + 第八章追加 8.5 复核结论。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

p = r"scripts\build_project_report_docx.py"
c = open(p, encoding="utf-8").read()

# ---- 1. 第四章状态表：前端生产化行更新为已完成 ----
old_fe = '''        ["前端生产化", "六大页面功能完整", "API 地址环境变量化、医院下拉登录、工作台服务端分页、通知中心去 mock、i18n 收口、去外链头像", "内网部署前需一轮工程化收口"],'''
new_fe = '''        ["前端生产化", "（2026-10-31 已收口 ✅）六大页面完整 + API 地址环境变量化（VITE_API_BASE_URL/同源反代）+ 医院下拉登录（匿名公开端点）+ 工作台服务端分页（keyword 下推数据库端）+ 通知中心去 mock 并实现管道任务完成本地会话推送（含耗时）+ i18n 高频路径收口 + 本地 SVG 头像去外链", "静态布局文案 i18n 余量（低频，下期）", "内网部署工程化收口已完成，随生产部署四包一并交付"],'''
assert old_fe in c, "frontend row"
c = c.replace(old_fe, new_fe, 1)

# ---- 2. 第四章状态表：生产部署行更新 ----
old_dp = '''        ["生产部署", "docker-compose 开发环境、CI、冒烟脚本", "生产级部署编排、前端内网部署、HTTPS/反向代理、密钥管理", "当前为本机联调可演示状态"],'''
new_dp = '''        ["生产部署", "（2026-10-31 已收口 ✅ 编码完成）docker-compose 开发环境 + CI + 冒烟脚本 + 生产级部署编排（docker-compose.prod.yml 七服务：nginx TLS 反代/api/worker/ES+Redis 认证）+ 前端内网部署（nginx 托管 dist/同源反代）+ HTTPS/反向代理（UseForwardedHeaders KnownProxies/HSTS）+ 密钥管理（secrets.env 模板/DataProtection 持久化卷）+ deploy.ps1 一键部署", "尚未在真实生产环境执行过一次完整部署（编码完成、执行待按检查清单逐项验证）", "编码层闭环，按部署检查清单执行即可上线"],'''
assert old_dp in c, "deploy row"
c = c.replace(old_dp, new_dp, 1)

# ---- 3. 第四章状态表：知识库治理行更新 ----
old_kb = '''        ["知识库质量治理", "source-manifest 版本清单、重复/笔误清单已知、词面补丁多轮", "版本化校对流程与维护 UI；笔误/重复条目批量清洗", "知识库缺口是漏推荐主因之一（E3 类 1603 名中 1216 名非 Z 章）"],'''
new_kb = '''        ["知识库质量治理", "（2026-10-31 已收口 ✅ Q1-Q4）增量批次回写（9078 行 batch-9001/9002 + manifest v2 幂等重放验证）+ kb-doctor 巡检命令（歧义 1172/重复 0/孤儿码 5860/E3 复测清单 359 名）+ 三索引表停用开关（is_enabled 迁移 + PATCH 端点 + 审计留痕）+ 维护 UI 停用/启用按钮（12 处 i18n）", "巡检结果的分诊处置（歧义码保留裁定/补词工作流）持续进行；E3 补词 1216 名非 Z 章仍是漏推荐主因", "治理工具链闭环，数据侧补词为长期工作"],'''
assert old_kb in c, "kb row"
c = c.replace(old_kb, new_kb, 1)

# ---- 4. 第四章状态表：LLM 行更新 ----
old_llm = '''        ["LLM 一致性校验", "本地 0.5B 模型实接、逐候选判据、Schema 校验", "证据引用维拿不到分（模型能力上限）；需换更强模型重评", "LLM 维仅部分生效，暂不作精度主张"],'''
new_llm = '''        ["LLM 一致性校验", "（2026-10-31 已升级 ✅）qwen2.5:3b-instruct（GPU）实接、逐候选判据、Schema 校验；证据引用维恢复可得分（6/6）；万条 A/B 排序税归零（Top1 换码 15 条/0.16%，0.5B 时代 16.1%）", "JZEY 临床口语口径的 3B 复测未跑（当前为 ICD10CAP 压力语料结论）", "LLM 维完整生效，精度主张待 JZEY 复测确认"],'''
assert old_llm in c, "llm row"
c = c.replace(old_llm, new_llm, 1)

# ---- 5. 第五章 5.2 高优先项更新（安全收尾/前端生产化已完成）----
old_p52 = '''numbered("安全收尾：生产 JWT Key 密钥管理注入、渗透级复核（登录锁定/限流/权限矩阵已于 2026-10-30 收口交付）。")
numbered("前端生产化清单（WP-B4-3）：API 地址环境变量化、登录改医院下拉、工作台服务端分页、通知中心去 mock、i18n 硬编码收口、登出清理医院上下文、去 dicebear 外链头像。")'''
new_p52 = '''numbered("安全收尾：生产 JWT Key 密钥管理注入（secrets.env 模板已就绪）、渗透级复核（登录锁定/限流/权限矩阵/生产部署编排已于 2026-10-30~31 全部交付）。")
numbered("JZEY 临床口语口径的 3B 复测（ICD10CAP 压力口径 A/B 已跑完：排序税归零；JZEY 口径待跑）。")'''
assert old_p52 in c, "p52 anchor"
c = c.replace(old_p52, new_p52, 1)

# ---- 6. 第八章追加 8.5 复核结论 ----
anchor85 = "para()\np = doc.add_paragraph()\nr = p.add_run(\"总结：\")"
review_section = '''doc.add_heading("8.5 源码复核结论（10-31 晚，对照第八章逐项）", level=2)
para("对照本报告各章声明，逐项在当前 HEAD（cb1565f）源码与数据面复核，结果如下：")
add_table(
    ["汇报条目", "源码/数据面证据", "复核结论"],
    [
        ["RBAC 五模板/锁定 423/限流 429/审计", "RoleMatrix/LockoutOptions/AccountLockedException/LoginRateLimitOptions 源码在位；AuthService 抛 AccountLockedException；login 端点挂 RequireRateLimiting；unlock 端点+审计在位", "✅ 已解决"],
        ["LLM 3B 升级", "api/worker 两处 appsettings 均 qwen2.5:3b-instruct；万条 A/B 排序税归零（15 条换码）", "✅ 已解决（JZEY 复测待跑）"],
        ["CAP 对比 Top1 上升", "终版工作簿 + 数据面可复现（A/B 轮 9846 推荐/1000 任务终态在库）", "✅ 已解决"],
        ["治理 WP-A 增量回写", "batch-9001/9002（9078 行）+ manifest kb-2026-10-31.1；icd_index 60346 行在库", "✅ 已解决"],
        ["治理 WP-B kb-doctor", "巡检命令实跑产出 4 份 CSV（歧义 1172/重复 0/孤儿 5860/E3 359）", "✅ 已解决"],
        ["治理 WP-C 停用开关", "三表 is_enabled 迁移已应用（数据库列数验证 =3）；PATCH 端点 + 冒烟（停用→审计→启用）", "✅ 已解决"],
        ["治理 WP-D 维护 UI", "12 处停用/启用按钮 + i18n + DTO；typecheck 通过", "✅ 已解决"],
        ["前端① API 环境变量化", "VITE_API_BASE_URL 参数化 + 双 env 文件", "✅ 已解决"],
        ["前端② 医院下拉登录", "匿名 GET /hospitals/public 端点 + Login.vue 下拉（含手动回退）", "✅ 已解决"],
        ["前端③ 工作台服务端分页", "page/pageSize/keyword 数据库端下推 + 前端防抖搜索；typecheck 通过", "✅ 已解决"],
        ["前端④ 通知中心去 mock", "store 空初始化（mock 残留 0）；管道完成/失败/超时三态本地会话推送（含耗时）", "✅ 已解决"],
        ["前端⑤ i18n 收口", "高频路径硬编码扫描仅 1 处（已收口）；静态布局文案余量低频，列入下期", "✅ 基本解决"],
        ["前端⑥ 去外链头像", "本地 SVG（hash 取色+首字母）；dicebear 引用全清", "✅ 已解决"],
        ["生产部署四包", "prod 编排/nginx conf/deploy.ps1/secrets 模板/DataProtection 卷全部在位；构建 0 错误", "✅ 编码完成（真实环境执行待验证）"],
    ],
    widths=[4.6, 7.6, 3.8],
    first_col_bold=True,
)
para()
para("复核后仍待解决的项：① JZEY 临床口语口径的 3B 复测（管线就绪，约 8 小时）；"
     "② 知识库数据侧补词（E3 类 1216 名非 Z 章，长期工作）；"
     "③ 生产环境真实部署执行（按检查清单逐项）；"
     "④ HIS/EMR 接口化对接（方案未启动）；⑤ Phase 2 启动评审。")

para()
p = doc.add_paragraph()
r = p.add_run("总结：")'''
assert anchor85 in c, "ch8 summary anchor"
c = c.replace(anchor85, review_section, 1)

open(p, "w", encoding="utf-8").write(c)
print("chapter 4 rows + 8.5 review section updated")
