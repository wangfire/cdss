# -*- coding: utf-8 -*-
"""生成《CDSS 智能编码系统 项目分析汇报》Word 文档。"""
import json
import os

from docx import Document
from docx.enum.table import WD_TABLE_ALIGNMENT
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.oxml.ns import qn
from docx.shared import Cm, Pt, RGBColor

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, "..", "docs", "2026-10-30-CDSS智能编码系统项目分析汇报.docx"))

ACCENT = RGBColor(0x1F, 0x4E, 0x79)
GREEN = RGBColor(0x2E, 0x7D, 0x32)
ORANGE = RGBColor(0xB26, 0x60, 0x00) if False else RGBColor(0xB2, 0x6A, 0x00)
GRAY = RGBColor(0x66, 0x66, 0x66)
RED = RGBColor(0xC0, 0x39, 0x2B)

doc = Document()

# ---------- base styles ----------
style = doc.styles["Normal"]
style.font.name = "Calibri"
style.font.size = Pt(10.5)
style.element.rPr.rFonts.set(qn("w:eastAsia"), "微软雅黑")
style.paragraph_format.space_after = Pt(4)
style.paragraph_format.line_spacing = 1.3

for name, size in (("Heading 1", 16), ("Heading 2", 13), ("Heading 3", 11.5)):
    st = doc.styles[name]
    st.font.name = "Calibri"
    st.element.rPr.rFonts.set(qn("w:eastAsia"), "微软雅黑")
    st.font.size = Pt(size)
    st.font.color.rgb = ACCENT
    st.font.bold = True
    st.paragraph_format.space_before = Pt(10)
    st.paragraph_format.space_after = Pt(5)

for sec in doc.sections:
    sec.top_margin = Cm(2.2)
    sec.bottom_margin = Cm(2.2)
    sec.left_margin = Cm(2.4)
    sec.right_margin = Cm(2.4)


def para(text="", bold=False, size=None, color=None, align=None, space_after=None, italic=False):
    p = doc.add_paragraph()
    if align is not None:
        p.alignment = align
    if space_after is not None:
        p.paragraph_format.space_after = Pt(space_after)
    r = p.add_run(text)
    r.bold = bold
    r.italic = italic
    if size:
        r.font.size = Pt(size)
    if color:
        r.font.color.rgb = color
    return p


def bullet(text, bold_prefix=None, level=0):
    p = doc.add_paragraph(style="List Bullet" if level == 0 else "List Bullet 2")
    if bold_prefix:
        r = p.add_run(bold_prefix)
        r.bold = True
        p.add_run(text)
    else:
        p.add_run(text)
    return p


def numbered(text, bold_prefix=None):
    p = doc.add_paragraph(style="List Number")
    if bold_prefix:
        r = p.add_run(bold_prefix)
        r.bold = True
        p.add_run(text)
    else:
        p.add_run(text)
    return p


def add_table(headers, rows, widths=None, first_col_bold=False):
    t = doc.add_table(rows=1, cols=len(headers))
    t.style = "Light Grid Accent 1"
    t.alignment = WD_TABLE_ALIGNMENT.CENTER
    hdr = t.rows[0].cells
    for i, h in enumerate(headers):
        hdr[i].text = ""
        r = hdr[i].paragraphs[0].add_run(h)
        r.bold = True
        r.font.size = Pt(9.5)
    for row in rows:
        cells = t.add_row().cells
        for i, v in enumerate(row):
            cells[i].text = ""
            r = cells[i].paragraphs[0].add_run(str(v))
            r.font.size = Pt(9)
            if first_col_bold and i == 0:
                r.bold = True
    if widths:
        for i, w in enumerate(widths):
            for row in t.rows:
                row.cells[i].width = Cm(w)
    return t


def kv_note(label, text):
    p = doc.add_paragraph()
    r = p.add_run(label + "：")
    r.bold = True
    r.font.size = Pt(9)
    r.font.color.rgb = GRAY
    r2 = p.add_run(text)
    r2.font.size = Pt(9)
    r2.font.color.rgb = GRAY


# ================= 封面 =================
for _ in range(5):
    doc.add_paragraph()
para("CDSS 智能编码系统", bold=True, size=30, color=ACCENT, align=WD_ALIGN_PARAGRAPH.CENTER, space_after=6)
para("项目分析汇报", bold=True, size=20, color=ACCENT, align=WD_ALIGN_PARAGRAPH.CENTER, space_after=30)
para("—— 设计功能清单 · 已完成交付 · 待完成规划 ——", size=12, color=GRAY, align=WD_ALIGN_PARAGRAPH.CENTER, space_after=40)
doc.add_paragraph()
for label, value in (("汇报日期", "2026 年 11 月 1 日"), ("汇报人", "王飞"),
                     ("代码分支", "codex-phase2-coding-mvp（最近提交 2026-11-01）"),
                     ("项目定位", "面向病案编码员的 ICD-10 / ICD-9-CM-3 智能编码推荐与人工审核系统")):
    p = doc.add_paragraph()
    p.alignment = WD_ALIGN_PARAGRAPH.CENTER
    r = p.add_run(f"{label}：")
    r.bold = True
    r.font.size = Pt(11)
    r2 = p.add_run(value)
    r2.font.size = Pt(11)
doc.add_page_break()

# ================= 目录 =================
doc.add_heading("目录", level=1)
toc_items = [
    "一、项目概述与总体架构",
    "二、系统设计功能全景（含实现状态总表）",
    "三、已完成功能（分模块详述）",
    "四、部分完成 / 进行中的功能",
    "五、待完成功能与建议优先级",
    "六、质量保障与实测数据",
    "七、风险与建议",
    "八、本期收口成果（2026-10-30 ~ 10-31）",
]
for it in toc_items:
    p = doc.add_paragraph(it)
    p.paragraph_format.space_after = Pt(6)
doc.add_page_break()

# ================= 一、项目概述 =================
doc.add_heading("一、项目概述与总体架构", level=1)

doc.add_heading("1.1 项目定位", level=2)
para("本系统（HospitalAi / CDSS 智能编码系统）面向医院病案室与编码员，模拟人工“翻 ICD 索引书”的编码动作："
     "定主导词 → 逐级下钻 → 展开索引编码 → 多通道召回合并 → 打分取 Top3 → 给出置信度分档与证据链。"
     "系统坚持三条核心原则：宁缺毋错（无安全候选就不推荐）、全程可解释（每条推荐带推理路径与病历原文证据）、"
     "人工优先（已确认的编码永不被自动重跑覆盖）。")

doc.add_heading("1.2 技术栈与总体架构", level=2)
add_table(
    ["层次", "技术选型", "说明"],
    [
        ["后端 API", ".NET 10（HospitalAi.Api，Minimal API）", "75 个 REST 端点：编码任务、推荐查询、人工审核、知识库、认证、权限、工作台"],
        ["推荐流水线", ".NET 10（HospitalAi.Worker，V2.2-Lite）", "12 阶段流水线：质量门 → 输入解析 → 事实抽取 → 证据链 → 精确/索引/全文/向量检索 → 规则 → 精排 → 评分 → 落库"],
        ["数据库", "SQL Server（EF Core，23 个迁移）", "任务、输入、候选、推荐、证据、规则、审核结论、索引库（icd_index / tumor_index / cm3_index）等全量持久化"],
        ["检索引擎", "Elasticsearch 8.14.3 + IK 中文分词", "约 5.3 万条 ICD-10 / ICD-9 编码知识库，BM25 词法检索 + dense_vector 向量检索；生产启用 xpack.security 认证"],
        ["AI 模型", "本地推理（GPU + ONNX/CPU 混合）", "bge-small-zh-v1.5（语义向量）、bge-reranker-base（重排）、Ollama qwen2.5:3b-instruct（LLM 一致性校验，GPU 推理），向量/重排为 ONNX/CPU 小模型"],
        ["消息与缓存", "RabbitMQ（MassTransit）+ Redis + Outbox", "异步任务分发、失败重试、中断恢复；写路径统一“落任务 + Outbox → MQ → Worker”"],
        ["前端", "Vue 3 + Vite + Pinia（web-ui/apps/admin）", "登录、仪表盘、编码工作台、病例录入、知识库管理、账户与三级权限配置六大页面"],
        ["多租户", "医院级数据隔离", "所有业务接口按 HospitalId 隔离；JWT Bearer 认证"],
    ],
    widths=[3.2, 5.2, 7.6],
    first_col_bold=True,
)

doc.add_heading("1.3 工程与仓库结构", level=2)
add_table(
    ["目录", "内容"],
    [
        ["src/", "六个 .NET 项目：Api / Application / Contracts / Domain / Infrastructure / Worker（分层架构）"],
        ["web-ui/", "前端 pnpm monorepo：apps/admin 主管理端 + ui / utils / styles 等公共包"],
        ["tests/", "5 个 .NET 测试项目（Api / Application / Domain / Infrastructure / Worker）+ knowledge（Python）+ smoke 冒烟脚本"],
        ["tools/", "HospitalAi.Tools（caliber 精度复算等命令）、IcdIndexDiag（离线逐名诊断工具）、知识库 SQL 脚本"],
        ["data/knowledge/", "ICD-10 / ICD-9-CM-3 字典、编码规则、肿瘤索引、ICD 索引批次导入数据（约 15MB，随仓库入库）"],
        ["models/", "本地模型：bge-small-zh-v1.5、bge-reranker-base"],
        ["deploy/", "docker-compose 开发环境（SQL Server / Redis / RabbitMQ / ES）"],
        [".github/", "CI 流水线（backend build+test 含 SQL Server 集成测试、frontend build）"],
        ["docs/", "13 份阶段方案 / 汇报 / 口径规范文档 + superpowers/reports 验证报告"],
    ],
    widths=[3.6, 12.4],
    first_col_bold=True,
)

doc.add_heading("1.4 阶段演进路线", level=2)
add_table(
    ["阶段", "时间", "内容", "验证"],
    [
        ["Phase 1 平台底座", "2026-09-15", "RBAC、医院隔离、任务管理、OTel 可观测", "39/39"],
        ["Phase 2 编码 MVP", "2026-09-17", "字典导入、基线推荐、证据链、人工审核闭环", "47/47（续验 52/52）"],
        ["V2.2-Lite 流水线重构", "2026-09-20", "11 步端到端流水线 + 七维评分", "89/89"],
        ["病例录入 + 真实数据验证", "2026-09-21", "手工录入即时推荐；501 名真实患者批量验证", "501/501"],
        ["ICD 索引驱动改造", "2026-09-24 ~ 09-30", "诊断通道切换为索引书两段式链路", "M5 验收"],
        ["生产化收口（B1/B2）", "2026-10 上旬", "成果入库、CI、Swagger、安全四项、全异步化", "CI 全绿"],
        ["推荐能力跃升（B3）", "2026-10 中旬", "向量检索、Rerank、LLM 实接、肿瘤索引、CM3 手术索引", "万条回归 + A/B 报告"],
        ["前端完善", "2026-10 下旬", "账户设置、三级权限树、仪表盘权限显隐", "typecheck 通过"],
    ],
    widths=[3.6, 2.6, 7.0, 2.8],
    first_col_bold=True,
)
doc.add_page_break()

# ================= 二、设计功能全景 =================
doc.add_heading("二、系统设计功能全景（含实现状态总表）", level=1)
para("下表为系统整体设计的功能清单及当前实现状态。状态口径：● 已完成（已实现并经测试/数据验证）；"
     "◐ 部分完成（主干可用，尚有明确缺口）；○ 待完成（设计已规划，未实施）。")

doc.add_heading("2.1 平台与工程底座", level=2)
add_table(
    ["模块", "设计功能点", "状态", "说明"],
    [
        ["认证登录", "账号密码登录（PBKDF2）、JWT 令牌、修改密码、登录态查询、账号级登录锁定（423 + 管理解锁）、登录接口 IP 限流（429）", "●", "HS256，120 分钟有效期；锁定默认 5 次失败锁 15 分钟，失败计数成功即清零"],
        ["RBAC 权限", "用户/角色管理、角色分配、菜单-功能-按钮三级权限树、正式权限矩阵（5 个内置角色模板 + 存量种子收敛 + 创建角色套用模板）", "●", "2026-10-30 收口：模板口径含 USER_ADMIN 不含 role.permission 写权防自提权闭环；67 个业务端点授权审计零遗漏并测试固化"],
        ["多租户隔离", "全部业务接口按医院隔离、编码二进制排序比较", "●", "X-Hospital-Id / JWT hospitalId 双通道"],
        ["异步任务", "落任务 + Outbox → MQ → Worker 执行、失败重试、中断补偿重投", "●", "双路径并发已消除（2026-10 拍板方案 A）"],
        ["可观测", "OpenTelemetry 指标（HTTP/SQL/消费/流水线）、Trace 全链路留痕、DEGRADED 降级显式标记", "●", "降级口径（NO_VECTOR / NO_BM25 / NO_CM3_INDEX）不伪造分数"],
        ["CI/CD", "GitHub Actions：后端构建+全量测试（含 SQL Server 服务容器）、前端构建", "●", "红测阻断合并"],
        ["接口文档", "Swagger / OpenAPI 全端点覆盖", "●", "统一 ApiEnvelope 响应信封"],
        ["部署", "docker-compose 开发环境一键启动；冒烟脚本", "◐", "开发环境完备；生产部署编排、前端内网部署待做（B4）"],
        ["数据安全", "身份防伪造、JWT Key 加固、明文凭据清理、端点授权矩阵（401/403 参数化集成测试固化）", "◐", "安全四项已关闭；生产密钥管理与渗透级加固待最终收口"],
    ],
    widths=[2.6, 6.4, 1.2, 6.0],
)

doc.add_heading("2.2 智能编码推荐核心（AI 流水线 V2.2-Lite）", level=2)
add_table(
    ["模块", "设计功能点", "状态", "说明"],
    [
        ["数据质量门", "文书缺失/为空/重复哈希/出入院时间矛盾拦截，中文可读提示", "●", "Step 1 即拦截，不产出“差不多”的推荐"],
        ["文书解析", "脱敏文书段落化；首页出院/入院诊断、手术操作三段结构化输入", "●", "四类输入来源：FRONT_PAGE / ADMISSION_PAGE / DOCUMENT_AUTO / PROCEDURE_AUTO"],
        ["临床事实抽取", "否定表达（未见/排除/否认）、病程（急性/慢性）、确定性（疑似/待查）、侧别部位识别", "◐", "否定/时间/确定性分级已接入规则上下文（B3-4）；细粒度优化持续迭代"],
        ["ICD-10 索引通道", "主导词召回（席位 12）→ 逐级下钻（新字符增益 + 部位/病程门控）→ 见/另见改道 → 7 种索引写法展开", "●", "5.1 万行 icd_index；末位“病/疾病”泛词兜底 ×0.8 显式标注"],
        ["肿瘤索引通道", "肿瘤部位表（1533 行）+ 裸形态学码 M8xx/3 → 行为码→部位码动态查找", "●", "tumor_index 五码并排存列；形态学入口保底席位"],
        ["ICD-9-CM-3 手术索引通道", "CM3 主导词（席位 20）→ 子树修饰词下钻 → 见/另见/见亚目改道 → 前缀展开细码", "●", "1.97 万行 cm3_index；同族标题反查替代最泛码回退"],
        ["字典标题直达", "索引零产出或链路走错时，输入对字典标题逐字相等/包含直达", "●", "诊断侧与手术侧均已交付，开关默认开启、可灰度关断"],
        ["Exact 精确匹配", "规范化后与标准名称/同义词完全一致直接召回", "●", "等义词换算（霉菌病=真菌病等）"],
        ["BM25 全文检索", "ES IK 分词多字段打分，连续片段覆盖率门槛（Ratio≥0.6 且 MaxSegment≥3）", "●", "失败显式 DEGRADED.NO_BM25，不静默少推荐"],
        ["向量语义检索", "bge-small-zh ONNX CPU 灌库 + 查询侧语义召回，与索引/Exact 混合去重", "●", "Top1 增量最大单项（+798/万条）；ES dense_vector"],
        ["Rerank 精排", "bge-reranker-base 对候选池前 8 条交叉编码重排", "●", "推理线程配额限核（与 LLM 同机不抢核）"],
        ["规则引擎", "糖尿病星码、部位冲突、病程互斥、极性（阳性/阴性）、(+)/(-) 改写等临床规则", "●", "内置规则集持续扩充中（见待办）"],
        ["LLM 一致性校验", "逐候选解释 + supported JSON Schema 校验 + 2 次重试；回显码在池内核验", "●", "本地 qwen2.5:3b-instruct（GPU）；证据引用维已恢复可得分（10-31 升级）"],
        ["七维评分", "Exact .15 / Semantic .15 / Retrieval .10 / Rerank .10 / Rule .15 / Evidence .20 / LLM .15，缺失维重归一", "●", "score_profile 记录参与口径，跨口径不比较"],
        ["置信度策略", "HIGH_CONFIDENCE / NEED_REVIEW / NO_SAFE_RECOMMENDATION 三档", "●", "总分≥0.85 且证据≥0.75 且 Margin≥0.10 才给高置信"],
        ["候选控制", "每输入 ≤3 候选、一族一席、同族名次上限、推荐版本化与软失效（STALE）", "●", "精度优先、可回溯版本链"],
        ["证据链", "按文书字符偏移定位段落，入院/出院证据互不串扰，每条 ≤3 条按分排序", "●", "证据维权重最高（0.20）"],
        ["人工确认保护", "已确认推荐不被自动重跑覆盖", "●", "final_coding_result 引用保护"],
    ],
    widths=[2.6, 6.4, 1.2, 6.0],
)

doc.add_heading("2.3 业务应用（前端页面）", level=2)
add_table(
    ["页面", "设计功能点", "状态", "说明"],
    [
        ["登录页", "账号密码 + 医院 ID 登录、演示账号提示", "●", "JWT 存储、登出清理医院上下文"],
        ["仪表盘", "任务/推荐统计、知识库概况卡片按权限分模块显隐、大屏并格等高布局", "●", "2026-10-28 最新交付"],
        ["编码工作台", "任务列表搜索/页签过滤；推荐三区页签（入院诊断/诊断/手术）分组展示候选与置信度；证据链悬停面板；运行记录抽屉回放 12 步骤耗时/降级/质量问题", "●", "证据链“→ 标准名”标注匹配关系"],
        ["病例录入", "三栏工作台：基本信息 + 诊断/手术录入 + 右侧即时推荐；质量约束（病案号唯一、住院次数校验、出院诊断必填）", "●", "已全异步化：提交受理 → 轮询取结果，消除双路径并发"],
        ["知识库管理", "ICD 索引 / 肿瘤索引 / CM3 索引 / 字典 / 同义词 / 编码规则的浏览、查询、导入、层级下钻、导入后缓存失效", "●", "cm3_index 19671 行已入库并可视化管理"],
        ["账户设置", "用户管理、角色管理、新建角色弹窗、权限配置三级树（菜单/功能/按钮）", "●", "2026-10-26/27 交付"],
        ["错误页", "401 / 403 / 404 统一错误页", "●", "—"],
        ["审核统计报表", "推荐采纳率、平均耗时、导出编码结论、多次就诊时间线", "○", "设计规划，未实施（低优先）"],
    ],
    widths=[2.6, 6.4, 1.2, 6.0],
)

doc.add_heading("2.4 知识库与数据工程", level=2)
add_table(
    ["模块", "设计功能点", "状态", "说明"],
    [
        ["字典管理", "ICD-10（2026 版）/ ICD-9-CM-3 字典导入与版本对齐", "●", "code-systems / medical-codes 导入端点"],
        ["ICD 索引库", "5.1 万行索引书词条入库，层级/跳转/别名解析，批次导入", "●", "icd-index 批次 JSON 随仓库"],
        ["肿瘤索引库", "1533 行部位表建表导入 + 14 条部位词面别名行补缺", "●", "2026-10-14 方案落地"],
        ["CM3 手术索引库", "19671 行建表、导入端点、分页检索、子树查询、知识库页签", "●", "2026-10-16 起，B0→B7 里程碑全部完成"],
        ["同义词库", "icd_term_synonym 等义词/别名导入，参与主导词换算", "●", "如 霉菌病=真菌病"],
        ["知识源版本治理", "source-manifest.json 导入前置检查、重复/笔误清洗", "◐", "manifest 惯例已有；版本化校对流程与维护 UI 待完善"],
        ["规则库治理", "890 条待确认医保规则分诊报告（可自动映射/人工评审/放弃）", "◐", "分诊报告已产出；coding_rule 扩充与数字化待续"],
        ["向量 embedding 灌库", "medical_code 标题/检索文本 → embedding 写入 ES dense_vector", "●", "离线批量 + 导入增量更新"],
        ["精度评测工具", "caliber 命令（L0-L4 分层口径、E1-E5 冻结剔除判据）、IcdIndexDiag 离线逐名/批量/清空归因", "●", "万条回归可复算、可 diff"],
    ],
    widths=[2.6, 6.4, 1.2, 6.0],
)

doc.add_heading("2.5 集成与对外能力", level=2)
add_table(
    ["模块", "设计功能点", "状态", "说明"],
    [
        ["HIS/EMR 对接", "患者/文书/回写三类接口化设计 + 映射表", "○", "方案设计阶段（WP-B4-4），当前经 SQL 导入脚本过渡"],
        ["首页手术明细导入", "源库手术信息按 ShunX 保序写入结构化输入（防真值泄漏）", "◐", "导入脚本已有；结构化 PROCEDURE 输入与索引通道对接基本完成"],
        ["审核结论回写", "final-results/submit 最终编码结论落库", "●", "人工闭环已完成"],
        ["开放 API", "REST + JWT + Swagger，医院内网可调用", "◐", "接口齐备；正式对外发布与 SDK 文档未启动"],
    ],
    widths=[2.6, 6.4, 1.2, 6.0],
)
doc.add_page_break()

# ================= 三、已完成功能 =================
doc.add_heading("三、已完成功能（分模块详述）", level=1)

doc.add_heading("3.1 平台基础与工程底座", level=2)
bullet("登录认证与 JWT：PBKDF2-SHA256（10 万轮）密码哈希、HS256 令牌（sub/hospitalId/username/jti 声明）、登录/登出/改密/当前用户四端点；RequestContextMiddleware 解析 Bearer 覆盖请求上下文，校验失败回退头认证以兼容旧测试。")
bullet("RBAC 与权限树：app_user / app_role / app_user_role 三表 + 角色权限（2026-10-26 迁移 RolePermissions）；前端权限配置改为菜单/功能/按钮三级树并最小粒度化（2026-10-27 提交）。")
bullet("正式权限矩阵（2026-10-30 收口）：RoleMatrix 五个内置角色模板（编码员/临床医生/病案审核/知识库管理员/账户管理员，USER_ADMIN 刻意不含 role.permission 写权防自提权闭环）；存量环境迁移幂等回填；创建角色一键套用模板；模板查询端点 /api/v1/security/role-templates。")
bullet("登录锁定与限流（2026-10-30 收口）：连续失败 5 次锁定 15 分钟（423 Locked + 剩余分钟数），达阈值当场锁定、成功登录清零、管理员一键解锁；登录接口按 IP 固定窗口限流（默认 60 次/分，超限 429）。")
bullet("鉴权全覆盖验证：67 个业务端点授权审计零遗漏，401/403 参数化集成测试 + 锁定/解锁/清零/模板/限流 7 个收口测试固化（ApiSecurityCloseoutTests 7/7 绿）。")
bullet("医院级多租户隔离：全部业务接口按 HospitalId 过滤，编码串二进制排序防大小写误匹配。")
bullet("异步任务链路：病例录入与推荐全部改为“落任务 + Outbox → RabbitMQ → Worker 执行”，HTTP 只返回受理结果，前端轮询取结果；Worker 启动时补偿重投中断遗留的 Running 任务；双路径并发写已消除（集成测试 + rowversion 冲突计数归零）。")
bullet("可观测与降级纪律：OpenTelemetry 全链路指标；ES 检索失败显式打 DEGRADED.NO_BM25 并告警，不再静默少推荐；向量/LLM/CM3 缺席均有显式降级标记。")
bullet("CI 与安全：GitHub Actions 后端（含 SQL Server 服务容器集成测试）+ 前端双 job；端点授权矩阵、JWT 加固（占位 Key 拒绝启动）、Swagger 全端点文档（WP-B2-2/B2-5 已提交落地）。")

doc.add_heading("3.2 智能编码推荐流水线（核心 AI 能力）", level=2)
para("端到端 12 阶段流水线已全部实跑并在真实数据上批量验证：", space_after=2)
numbered("质量门：文书缺失/空内容/重复哈希/时间矛盾即拦截。")
numbered("诊断输入解析：四类来源结构化入库（首页出院/首页入院/文书抽取/手术操作）。")
numbered("临床事实抽取：否定、病程、确定性、侧别部位；事实字段接入规则上下文。")
numbered("证据链生成：按入院/出院/手术段落字符偏移定位，段落隔离互不串扰。")
numbered("精确匹配检索：规范名/同义词一致直接召回。")
numbered("ICD 索引通道（诊断）+ CM3 索引通道（手术）：主导词召回 → 逐级下钻 → 见/另见改道 → 索引码展开（7 种写法 / 前缀展开 / 肿瘤部位表）。")
numbered("BM25 全文检索：IK 分词多字段，覆盖率门槛防“字面像、临床不对”。")
numbered("向量语义检索：bge-small-zh ONNX CPU，补索引通道结构性盲区（序号/待查尾缀、简称、状态类写法）。")
numbered("规则引擎：糖尿病星码、部位冲突、病程互斥、极性改写等；BLOCK 候选直接出局并记质量问题。")
numbered("Rerank 精排：bge-reranker-base 前 8 条交叉重排；线程配额限核（并发 10 跑批中位 1.05 秒/输入）。")
numbered("LLM 一致性校验：本地 Ollama 逐候选解释 + 可核验判据（回显码在池内 / 事实层否定叠加），输出不合规不给分。")
numbered("七维评分 + 置信度分档 + 推荐落库：版本化、软失效、一族一席、每输入 ≤3 候选。")
para("关键设计纪律（已在代码与测试中固化）：缺失维度不填默认值而是重归一化；降级显式标记绝不伪造分数；"
     "已确认推荐永不被自动覆盖；改动必过万条回归且硬线 becameEmpty=0（不允许任何一条输入因改动而失去推荐）。", space_after=2)

doc.add_heading("3.3 诊断编码通道（ICD-10）——索引书两段式", level=2)
bullet("5.1 万行 ICD 索引词条预处理：层级前缀、“见/另见/现译名”跳转、方括号与澄清圆括号、“或/，”并列别名，生成带权别名表。")
bullet("主导词召回：别名覆盖门控（≥0.6 且最长段≥3 或全覆盖）；泛化修饰词不作锚点；主导词黑名单按产出台账扩充（14 个 0 产出键禁用）；单字主导词按“输入结尾核心词”放行（M12，+34 Top1）。")
bullet("逐级下钻：子节点须带来 ≥2 连续字符新内容；同层取增益最大者；无码终点回退最近带码祖先；见/另见改道与本树下钻并列取覆盖多者；病因连接行透明化。")
bullet("语义门控：部位/人群限定词子集校验、病程互斥、澄清圆括号不硬约束、“A或B”取交集。")
bullet("编码落地：7 种索引写法解析 + 本地字典展开；肿瘤索引表（1533 行）支撑裸形态学码 → 部位码动态查找；索引码展不开时回退并显式标注。")
bullet("诊断侧字典标题直达与同族标题反查（2026-10-26 交付，与手术侧同模式）：整标题逐字相等走深度通道，包含匹配纯分数竞争 + 极大性规则。")

doc.add_heading("3.4 手术编码通道（ICD-9-CM-3）——CM3 索引两段式（2026-10 新交付）", level=2)
bullet("cm3_index 19671 行入库（0 级主导词 2855 条），建表迁移、导入端点、分页/子树检索、知识库页签、导入后缓存失效、DI 注册全部补齐。")
bullet("主导词席位 8→20、主导词即终点（死路主导词带码 1391 条）不降权、下钻子行部位冲突门控。")
bullet("同族标题反查替代“最泛码回退”（消灭 1348 条回退错码形态）；索引零产出时字典标题直达；直达常驻同台竞争。")
bullet("第一轮 A/B 实测（2026-10-24 报告）：临床首页名面 Top1 从 39.69% 提升到 97.72%，有推荐率 99%+，硬线 becameEmpty=0 全程保持、Top1 对→错仅 1 条（归因为对照源数据自相矛盾）。")

doc.add_heading("3.5 业务应用前端（Vue 3 管理端）", level=2)
bullet("编码工作台：任务列表搜索/状态页签；推荐三区页签（入院诊断/诊断/手术）按输入分组、主诊断置顶、置信度降序；证据链悬停面板（来源类型、得分、“→ 标准名”标注、段落隔离）；运行记录抽屉（12 步骤耗时、降级标记、质量问题、推荐条数，最近 20 次）。")
bullet("病例录入：三栏工作台（基本信息/诊断手术录入/AI 推荐即席展示），提交即全异步跑流水线，前端轮询取结果；数据质量约束（病案号院内唯一、同号异姓名拦截、住院次数校验、出院诊断至少一条）。")
bullet("知识库管理：ICD/肿瘤/CM3 三索引库 + 字典/同义词/规则的统一管理界面，支持导入、分页、层级下钻。")
bullet("仪表盘：统计卡片按权限分模块显隐、大屏并格等高布局（2026-10-28）。")
bullet("账户设置：用户与角色管理、新建角色弹窗、三级权限树配置（2026-10-26/27）。")
bullet("国际化（中/英）、主题设置、统一响应信封解析、登录态与医院上下文持久化。")

doc.add_heading("3.6 知识库与数据工程", level=2)
bullet("字典：ICD-10 / ICD-9-CM-3 2026 版已导入（ICD-9-CM-3 14388 个细码×双院区）。")
bullet("索引库：icd_index 51268 行、tumor_index 1533 行、cm3_index 19671 行；另有词面补丁（整形手术、Z42 随诊族、肿瘤部位别名 14 条等）。")
bullet("同义词：等义词换算表（霉菌病=真菌病、乳房=乳腺、癫癎=癫痫、呼衰=呼吸衰竭等按台账持续补充）。")
bullet("向量灌库：medical_code 标题 embedding 写入 ES dense_vector（JZEY 14059 行 / ICD10CAP 7057 行已出数）。")
bullet("工具链：caliber 精度复算命令（L0-L4 分层 + E1-E5 冻结剔除）、IcdIndexDiag 离线逐名诊断（--batch/--tree/--blank/--psubtree/--pblank）、万条跑批四层监控（超时重试/幂等续跑/终态标记/心跳看门狗）。")

doc.add_heading("3.7 质量验证与精度成果（重要里程碑）", level=2)
para("万条真实诊断名端到端回归（JZEY 语料，同一份 1 万条真值、E1-E5 冻结剔除判据）：", space_after=2)
add_table(
    ["指标", "2026-10-15 轮", "2026-10-20 轮", "变化", "归因"],
    [
        ["有推荐率（L0）", "65.51%", "100%", "+34.5pp", "向量通道纯补位 2566 行"],
        ["编码级 Top1（L0）", "14.71%", "27.67%", "+1296 条", "向量 +798 / 索引规则 +577 / LLM 排序税 −79"],
        ["Top3 命中（L0）", "1800 条", "3714 条", "+1914 条", "同上叠加"],
        ["类目级 Top1（L0）", "2690 条", "4362 条", "+1672 条", "同上叠加"],
        ["编码级 Top1（L1 可判定）", "20.62%", "31.08%", "+707 条", "同上"],
    ],
    widths=[4.2, 2.8, 2.8, 2.2, 4.0],
)
kv_note("诚实口径提醒", "向量通道命中率（23.8%/19.0%）低于索引通道（32.7%），其贡献是“能覆盖”而非“更准”；"
       "对照组 ICD10CAP 为随机字典题名压力语料，其 98.11% 只证明链路覆盖能力，对外汇报以临床面数字为准。")
para("手术通道（2026-10-24 A/B 报告）：临床首页名面 Top1 39.69% → 97.72%；D_CM3 字典名面 44.02% → 98.11%（压力口径，仅证明链路能力）。", space_after=2)
para("其他验证：100 条自建模拟病例验证器 95/95 通过；501 名真实患者批量推荐 501/501 成功、0 空推荐任务；"
     "证据链交叉污染扫描 0 违规；全项目非 DB 测试 247/247 绿（另有 51 个测试因缺测试库连接串属环境性跳过，与本轮改动无关）；"
     "CM3 单测 38/38 绿。", space_after=2)
doc.add_page_break()

# ================= 四、部分完成 =================
doc.add_heading("四、部分完成 / 进行中的功能", level=1)
add_table(
    ["功能项", "已有部分", "尚缺部分", "影响"],
    [
        ["RBAC 正式权限矩阵", "（2026-10-30 已收口 ✅）正式权限矩阵 5 个内置角色模板 + 存量种子收敛；67 个业务端点 401/403 授权审计零遗漏；账号级登录锁定（423/管理员解锁）与登录限流（429）上线", "无——尚缺三项已于 10-31 全部补齐（JWT Key 注入链路 / 登录失败与锁定触发审计留痕 / 登录页 423 倒计时），测试 14/14 绿", "演示与真实数据可用的权限基线已具备"],
        ["LLM 一致性校验", "（2026-10-31 已升级 ✅）qwen2.5:3b-instruct（GPU）实接、逐候选判据、Schema 校验；证据引用维恢复可得分（6/6）；万条 A/B 排序税归零（Top1 换码 15 条/0.16%，0.5B 时代 16.1%）", "JZEY 临床口语口径的 3B 复测未跑（当前为 ICD10CAP 压力语料结论）", "LLM 维完整生效，精度主张待 JZEY 复测确认"],
        ["知识库质量治理", "（2026-10-31 已收口 ✅ Q1-Q4）增量批次回写（9078 行 batch-9001/9002 + manifest v2 幂等重放验证）+ kb-doctor 巡检命令（歧义 1172/重复 0/孤儿码 5860/E3 复测清单 359 名）+ 三索引表停用开关（is_enabled 迁移 + PATCH 端点 + 审计留痕）+ 维护 UI 停用/启用按钮（12 处 i18n）", "巡检结果的分诊处置（歧义码保留裁定/补词工作流）持续进行；E3 补词 1216 名非 Z 章仍是漏推荐主因", "治理工具链闭环，数据侧补词为长期工作"],
        ["规则库", "内置临床规则集（糖尿病/部位/病程/极性）+ 890 条医保规则分诊报告", "规则数字化扩充（主要诊断选择、合并编码、星剑号）", "规则维覆盖有限"],
        ["生产部署", "（2026-10-31 已收口 ✅ 编码完成）docker-compose 开发环境 + CI + 冒烟脚本 + 生产级部署编排（docker-compose.prod.yml 七服务：nginx TLS 反代/api/worker/ES+Redis 认证）+ 前端内网部署（nginx 托管 dist/同源反代）+ HTTPS/反向代理（UseForwardedHeaders KnownProxies/HSTS）+ 密钥管理（secrets.env 模板/DataProtection 持久化卷）+ deploy.ps1 一键部署", "尚未在真实生产环境执行过一次完整部署（编码完成、执行待按检查清单逐项验证）", "编码层闭环，按部署检查清单执行即可上线"],
        ["前端生产化", "（2026-10-31 已收口 ✅）六大页面完整 + API 地址环境变量化（VITE_API_BASE_URL/同源反代）+ 医院下拉登录（匿名公开端点）+ 工作台服务端分页（keyword 下推数据库端）+ 通知中心去 mock 并实现管道任务完成本地会话推送（含耗时）+ i18n 高频路径收口 + 静态布局文案收口（管道阶段/降级/主题/aria 共 26 key 中英）+ 本地 SVG 头像去外链", "无——六条目已全部闭环（i18n 静态布局文案复扫 0 行硬编码）", "内网部署工程化收口已完成，随生产部署四包一并交付"],
        ["手术输入结构化", "CM3 通道已通；病例录入手术字段已支持", "源库首页手术明细全量结构化导入的验收台账（容器验证库 PROCEDURE 输入仅 2 行，真实历史库 4 行——数据重建后待放量补导）", "手术通道精度数字已好，但真实输入覆盖待放量"],
    ],
    widths=[3.2, 4.6, 4.8, 3.4],
)
doc.add_page_break()

# ================= 五、待完成 =================
doc.add_heading("五、待完成功能与建议优先级", level=1)

doc.add_heading("5.1 高优先（推荐能力与验收收口）", level=2)
numbered("LLM 名次权重单独评估：回收 LLM 排序税 −79 条 Top1（纯排序问题、风险低）；如纳入精度口径须先换更强模型。")
numbered("索引通道“下钻无终点”长尾数据侧补词：索引通道自身约 26% 结构性缺口（结节族、部位+性质待查族最集中），按族补索引行/别名行（已有成功先例）。")
numbered("诊断侧平移手术侧“反查 + 直达”模式：IndexCodeExpander 的回退最泛码同款问题仍在（IndexCodeExpander.cs:722-729），照 10-24 手术侧方案平移。")
numbered("索引编码族名次上限：同一索引族可占多席挤掉精确码（如 甲状腺肿 E04.9 族占 2 席把精确码顶到第 6 名），需全局排序单开一轮。")
numbered("向量通道人工抽检口径：按“字面同名/词面变形/需语义推断”三档抽样，把向量 rank1（890 条）的精度讲清楚。")
numbered("精度验收线最终拍板：编码级 Top1 ≥25% 已达成（27.67%），类目级 ≥50% 验收线挂在哪一层（L0/L1/新建）按口径规范 §7 拍板后正式签字。")

doc.add_heading("5.2 中优先（工程与平台）", level=2)
numbered("安全收尾：生产 JWT Key 密钥管理注入（secrets.env 模板已就绪）、渗透级复核（登录锁定/限流/权限矩阵/生产部署编排已于 2026-10-30~31 全部交付）。")
numbered("JZEY 临床口语口径的 3B 复测（ICD10CAP 压力口径 A/B 已跑完：排序税归零；JZEY 口径待跑）。")
numbered("知识库治理流程化：版本化校对流程、维护 UI、方括号等价词族（肛瘘括号别名稀释）全库标注。")
numbered("规则库扩充：与临床编码室共建，把主要诊断选择、合并编码、星剑号等 ICD 规则数字化（承接 890 条分诊报告）。")
numbered("归一化折叠全局评估：Unicode 罗马数字 vs ASCII、ECMO/HIFU 等缩写（约 30 条未命中归此类），涉及共用 TextMatch 需单轮评估。")

doc.add_heading("5.3 中低优先（对接与扩展）", level=2)
numbered("HIS/EMR 对接接口化设计成文（WP-B4-4）：患者/文书/回写三类接口 + 映射表，替代当前 SQL 导入脚本过渡态。")
numbered("手术侧同义词等价边打通：边已可导入，B3 之后单独一轮生效。")
numbered("CM3 括注双码 109 行语义人工复核（主[附]双码先只解析不出码）。")
numbered("审核统计报表与运营功能：推荐采纳率、平均耗时、编码结论导出、多住院次时间线。")

doc.add_heading("5.4 远期（Phase 2 阶段）", level=2)
numbered("Phase 2（全病历质控 + Agent）启动评审：范围重述、与 V2.2 基线 §26 差距对齐（WP-B4-5）。")
numbered("GPU POC 后向量/Rerank 换大模型重灌（链路与代码不变，仅重灌库）。")
numbered("手术编码自动终审（是否与诊断/手术次序联动、主刀填码对照）——属病案业务，另行拍板。")
doc.add_page_break()

# ================= 六、质量保障 =================
doc.add_heading("六、质量保障与实测数据", level=1)

doc.add_heading("6.1 测试体系", level=2)
add_table(
    ["层次", "内容", "状态"],
    [
        ["单元/集成测试", "5 个 .NET 测试项目，18 个测试文件、143+ Fact；含 SQL Server 集成测试（CI 服务容器）", "非 DB 247/247 绿"],
        ["安全收口专项测试", "67 端点匿名 401、只读令牌写端点 403、零权限读端点 403、登录锁定 423→解锁、失败计数清零、角色模板套用、登录限流 429（ApiSecurityCloseoutTests）", "7/7 绿"],
        ["CM3 专项单测", "主导词有效性双轨、前缀选择、双码解析、改道、扇出上限等 38 个用例", "38/38 绿"],
        ["知识库 Python 测试", "tests/knowledge 单元测试 + build_knowledge.py 自检", "随全量验证执行"],
        ["端到端冒烟", "tests/smoke/run.ps1 一键起 docker compose 全栈并回归", "可重复执行"],
        ["万条离线回归", "IcdIndexDiag 批量台账 + caliber 分层复算，硬线 becameEmpty=0", "每轮改动必跑"],
        ["人工抽查", "M5 已抽查 20 例；B3 要求 50 例校准", "20 例已执行，50 例进行中"],
    ],
    widths=[3.2, 9.2, 3.6],
)

doc.add_heading("6.2 关键实测数据汇总", level=2)
add_table(
    ["数据项", "数值"],
    [
        ["知识库规模", "icd_index 51,268 行；cm3_index 19,671 行；tumor_index 1,533 行；ICD-9-CM-3 字典 14,388 细码；编码规则、同义词持续扩充"],
        ["真实数据验证", "501 名真实患者批量推荐 501/501 成功、0 失败、0 空推荐任务；ACTIVE 推荐 19,656 条（09-21 时点）"],
        ["万条诊断名回归", "10,000 名 / 1,000 任务；当前 Top1 27.67%、Top3 37.14%、类目 Top1 43.62%、有推荐率 100%（L0 口径）"],
        ["手术通道回归", "临床名面 2,371 名 Top1 97.72%；字典名面 13,736 名 Top1 98.11%（压力口径）"],
        ["性能", "万条跑批并发 6：每输入约 1.05 秒（线程配额修复后）；LLM 全量重跑 11,418 输入 100% 完成、0 遗留失败"],
        ["证据链质量", "入院/出院双向交叉污染 0 条"],
    ],
    widths=[3.6, 12.4],
)

doc.add_heading("6.3 交付物与文档", level=2)
bullet("代码：分支 codex-phase2-coding-mvp，最近提交 2026-11-01；本期新增 16 个 commit（RBAC 收口/LLM 3B/CAP 对比/知识库治理/生产部署/前端生产化），主体成果已全部入库。")
bullet("数据库迁移：23 个 EF Core 迁移（InitialCreate → 登录锁定 20261030090000 / 权限矩阵种子 20261030091000 / 索引表停用开关 20261031140000），迁移链完整、可从空库重建。")
bullet("文档：17 份阶段方案/汇报/口径规范（docs/，含 RBAC 收口交付说明、LLM 3B A/B 报告、CAP 对比报告、生产部署/前端生产化/知识库治理三份方案），验证报告归档（docs/superpowers/reports/），面向病案室的流程讲解稿与可视化流程图（智能编码推荐流程说明.md / 流程图.html）。")
bullet("汇报材料：三大业务与架构流程 PPT（仓库根目录）。")
doc.add_page_break()

# ================= 七、风险与建议 =================
doc.add_heading("七、风险与建议", level=1)
add_table(
    ["风险", "说明", "建议对策"],
    [
        ["向量通道正确性依赖词面近似", "旧模型（0.5B）时代向量补位无法引用索引链路证据；3B 升级后 LLM 维恢复完整（向量通道本身不产证据链引用）", "把向量补位的 487 条列进人工抽检范围；对外同时公布“证据可回溯率”"],
        ["知识库覆盖缺口", "E3 类 1,603 名无锚点（其中 1,216 名非 Z 章属必须补词）；手术侧约 90 条族缺口", "数据侧补词专项 + 版本化治理流程"],
        ["同机 CPU 推理资源竞争", "ONNX 与 Ollama 抢核曾致单请求中位耗时 9.9 秒", "线程配额机制已修复（限 4 核）；生产部署沿用“同机核数预算”配置"],
        ["长跑批静默停摆", "历史上曾因未捕获 AbortError 静默死亡 12 小时", "四层监控已固化（超时重试/幂等续跑/终态标记/心跳看门狗），新批任务复用"],
        ["精度数字口径混用", "不同语料/口径数字差异巨大（14% vs 63.9%）", "严格执行口径规范：L0/L1 并列发布、E1-E5 冻结、压力语料免责声明"],
        ["密钥与安全欠账", "JWT Key 曾为占位符、dev 密码曾入 git 历史", "生产部署前完成安全四项复核 + 凭据轮换"],
    ],
    widths=[3.6, 6.4, 6.0],
)

doc.add_page_break()

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

doc.add_heading("8.5 源码复核结论（10-31 晚，对照第八章逐项）", level=2)
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
        ["前端⑤ i18n 收口", "高频路径硬编码扫描仅 1 处（已收口）；静态布局文案已于 10-31 收口（复扫 0 行）", "✅ 基本解决"],
        ["前端⑥ 去外链头像", "本地 SVG（hash 取色+首字母）；dicebear 引用全清", "✅ 已解决"],
        ["生产部署四包", "prod 编排/nginx conf/deploy.ps1/secrets 模板/DataProtection 卷全部在位；构建 0 错误", "✅ 编码完成（真实环境执行待验证）"],
        ["RBAC 尚缺三项（JWT 注入/锁定审计/倒计时）", "prod compose JwtAuth__Key 注入链路 + secrets 模板在位；SqlServerAuthService 写 app_user.login_failed / lockout_triggered 审计；Login.vue 423 倒计时完整链路；测试断言（失败 ≥5 + LOCKED + FAILED）14/14 绿", "✅ 已解决（10-31 补齐）"],
        ["前端 i18n 静态文案收口", "管道阶段/降级标记映射 15 key、主题切换 5 key（新建 theme.ts）、aria-label（新建 layout.ts）、第X次住院；复扫剩余 10 行全部为知识库示例数据（领域数据非 UI 文案）", "✅ 已解决（10-31，5f1a98c）"],
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
r = p.add_run("总结：")
r.bold = True
p.add_run("系统平台底座、AI 推荐流水线、三大索引通道（ICD-10 / 肿瘤 / CM3 手术）、前端六大页面、"
          "评测与工具链均已交付并经真实数据验证；诊断通道万条 Top1 达 27.67%（JZEY 临床口径）与 52.04%"
          "（CAP 压力口径，本期 +1.31pp），手术通道临床名面 Top1 达 97.72%。本期完成 RBAC 收口、"
          "LLM 升级 3B（排序税归零）、CAP 对比 Top1 确认上升、知识库治理四包落地。剩余工作集中在："
          "JZEY 口径 3B 复测、知识库数据侧补词、生产部署与前端工程化收口、HIS/EMR 接口化对接，"
          "以及 Phase 2（全病历质控 + Agent）的启动评审。")


r.bold = True
p.add_run("系统平台底座、AI 推荐流水线、三大索引通道（ICD-10 / 肿瘤 / CM3 手术）、前端六大页面、"
          "评测与工具链均已交付并经真实数据验证；诊断通道万条 Top1 达 27.67%（半年内从 7.5% 起步多轮跃升），"
          "手术通道临床名面 Top1 达 97.72%。剩余工作集中在：知识库数据侧补词、LLM 名次权重收口、"
          "生产部署与前端工程化收口、HIS/EMR 接口化对接，以及 Phase 2（全病历质控 + Agent）的启动评审。")

try:
    doc.save(OUT)
    print("saved:", OUT)
except PermissionError:
    # 旧文件正被 Word 打开锁定时，另存为带时间戳的新文件。
    import datetime
    stamp = datetime.datetime.now().strftime("%H%M")
    fallback = OUT.replace(".docx", f"-{stamp}.docx")
    doc.save(fallback)
    print("saved:", fallback, "（原文件被占用，请关闭 Word 后删除旧版）")