# -*- coding: utf-8 -*-
"""在总汇报 docx 脚本中插入第八章（2026-10-30 ~ 10-31 收口成果）并更新总结段。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

p = r"scripts\build_project_report_docx.py"
c = open(p, encoding="utf-8").read()

anchor = '''para()
p = doc.add_paragraph()
r = p.add_run("总结：")'''

chapter = '''doc.add_page_break()

# ================= 八、本期收口成果 =================
doc.add_heading("八、本期收口成果（2026-10-30 ~ 10-31）", level=1)
para("本章节记录最近两天的集中收口：RBAC 三缺口关闭、LLM 校验模型升级、CAP 院万条对比与知识库质量治理。"
     "全部成果已分批提交（8 个 commit），构建 0 错误、回归测试 14/14 绿。")

doc.add_heading("8.1 RBAC 收口：正式权限矩阵 + 登录锁定/限流 + 鉴权全覆盖", level=2)
add_table(
    ["缺口项", "交付内容", "验证"],
    [
        ["正式权限矩阵收敛", "RoleMatrix 五个内置角色模板（编码员/临床医生/病案审核/知识库管理员/账户管理员，USER_ADMIN 刻意不含 role.permission 写权防自提权闭环）+ 存量种子迁移幂等回填 + 创建角色一键套用模板", "模板与迁移同口径；集成测试断言"],
        ["鉴权策略全覆盖", "67 个业务端点授权审计零遗漏；401/403 参数化集成测试固化（匿名全 401、只读令牌写端点全 403、零权限读端点 403）", "ApiSecurityCloseoutTests 7/7 绿"],
        ["登录锁定与限流", "账号级锁定：连续失败 5 次锁 15 分钟（423 + 剩余分钟数），当场锁定、成功清零、管理员一键解锁；登录接口 IP 固定窗口限流 60 次/分（超限 429）", "423/429/解锁/计数清零测试全过"],
    ],
    widths=[3.4, 8.2, 4.4],
    first_col_bold=True,
)

doc.add_heading("8.2 LLM 校验模型升级：0.5B → 3B（GPU）", level=2)
add_table(
    ["指标", "0.5b（旧）", "1.5b", "3b（已切换）"],
    [
        ["Schema 校验通过", "6/6", "4/6（把 Schema 当输出）", "6/6"],
        ["cited_evidence 非空（证据引用维可得分）", "0/6（恒空）", "3/4", "6/6"],
        ["热态延迟 p50", "0.19s", "0.33s", "0.15~0.21s（GPU）"],
        ["显存（Q4 量化）", "0.4GB", "1.0GB", "1.9GB"],
    ],
    widths=[5.6, 3.6, 3.4, 3.4],
    first_col_bold=True,
)
para("万条 A/B（同环境同语料同规则，仅 LLM 开关不同）：**排序税归零**——Top1 换码仅 15 条（0.16%，"
     "0.5B 时代为 16.1%），becameEmpty=0，有推荐率零变化。LLM 维（权重 0.15）恢复完整生效。"
     "性能：GPU 化后单候选校验 0.15~0.21s（较 CPU 快 2 倍），万条 LLM-on 全量 8.5 小时。"
     "过程发现并修复：双 Ollama 安装多实例冲突（GPU 85°C 根因）、EF 迁移精度不一致（AutoMigrate 崩溃）。")

doc.add_heading("8.3 CAP 院万条对比：当前版 vs 1420 版 —— Top1 匹配率确认上升", level=2)
add_table(
    ["指标（n=10098）", "1420 版（10-26）", "当前版（10-31）", "变化"],
    [
        ["有推荐", "9264（91.74%）", "9593（95.00%）", "+329（+3.26pp）"],
        ["Top1 精确", "5123（50.73%）", "5255（52.04%）", "+132（+1.31pp）"],
        ["Top3 精确", "5154", "5262", "+108"],
        ["类目 Top1", "6132", "6435", "+303"],
        ["becameEmpty", "—", "0", "硬线达成"],
    ],
    widths=[4.0, 4.0, 4.0, 4.0],
    first_col_bold=True,
)
para("对比前提：原 LocalDB 知识库物理文件丢失，按仓库批次在容器库重建后，通过五轮补丁把原库独有的 "
     "9021 行增量词面行（诊断名逐字挂码兜底）全部重建，使两轮知识面完全对齐——差异只反映代码改进。"
     "逐名 diff：Top1 命中 0 流失 / 132 新增。口径提醒：ICD10CAP 为字典题名压力语料（度量链路能力），"
     "与 JZEY 临床口语口径（Top1 27.67%）不可互换。")

doc.add_heading("8.4 知识库质量治理（Q1-Q4 四工作包）", level=2)
add_table(
    ["工作包", "交付内容", "状态"],
    [
        ["WP-A 增量回写（Q1）", "库内 9078 行增量导出为规范批次 icd-index-batches-extra/batch-9001/9002.json；幂等重放验证通过（imported=0 updated=9078）；manifest 升级 kb-2026-10-31.1（含版本号/extra 批次/patches 登记/diffSummary）", "✅"],
        ["WP-B 质量巡检（Q2）", "kb-doctor 命令五项检查：歧义 1172 条 / 重复行 0 / 孤儿码 5860 / E3 复测清单 359 名，产出可分派 CSV 至 data/knowledge/reports/", "✅"],
        ["WP-C 停用开关（Q3）", "三索引表迁移加 is_enabled；快照构建与检索过滤停用行；PATCH /knowledge/{kind}-index/{id}/enabled + 审计留痕（冒烟：停用→审计→启用全通过）", "✅"],
        ["WP-D 维护 UI（Q4）", "知识库页 12 处停用/启用按钮（三索引树行内+树内）、knowledgeApi.setIndexEnabled、DTO 补 isEnabled、中英 i18n 四 key", "✅"],
    ],
    widths=[3.4, 9.6, 3.0],
    first_col_bold=True,
)

para()
p = doc.add_paragraph()
r = p.add_run("总结：")
r.bold = True
p.add_run("系统平台底座、AI 推荐流水线、三大索引通道（ICD-10 / 肿瘤 / CM3 手术）、前端六大页面、"
          "评测与工具链均已交付并经真实数据验证；诊断通道万条 Top1 达 27.67%（JZEY 临床口径）与 52.04%"
          "（CAP 压力口径，本期 +1.31pp），手术通道临床名面 Top1 达 97.72%。本期完成 RBAC 收口、"
          "LLM 升级 3B（排序税归零）、CAP 对比 Top1 确认上升、知识库治理四包落地。剩余工作集中在："
          "JZEY 口径 3B 复测、知识库数据侧补词、生产部署与前端工程化收口、HIS/EMR 接口化对接，"
          "以及 Phase 2（全病历质控 + Agent）的启动评审。")

'''

assert anchor in c, "summary anchor"
c = c.replace(anchor, chapter, 1)
open(p, "w", encoding="utf-8").write(c)
print("chapter 8 inserted, summary updated")
