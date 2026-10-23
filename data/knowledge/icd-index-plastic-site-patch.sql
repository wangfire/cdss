-- icd_index「整形手术」词面补丁（Z42 整形/随诊医疗族，1 行改词面 + 6 行部位子行）。
-- 根因：索引里 Z42.0~Z42.8 只挂在 外科手术(39972) → 再建(39995) 与 修复(43750) → 瘢痕组织(43751) 两条链路下，
--       而 外科手术 → 整形(40003) 这一支的子行只有 美容性 Z41.1 / 为体表不能接受的外貌 Z41.1 和两条
--       「- 见 外科手术，再建」指针（40004/40009）；病历与字典标题写的都是「涉及X整形手术的随诊医疗」，
--       主导词 外科手术 一个字都锚不住（输入词面是 整形手术，不是 外科手术）。
--
-- ① 给 0 级行 外科手术 补 整形/整形手术 词面别名。**必须用逗号并列**，不能用圆括号——
--   ParseAliases 对圆括号只产出「含括号整词」(外科手术整形整形手术) 与「剥括号」(外科手术) 两个键，
--   括号里的词永不成为召回键（实测 外科手术(整形)(整形手术) 对 26 名零变化）；
--   逗号/顿号段才逐段成键（首段 1.0，其余 0.85），且 0 级行的既有惯例就是逗号并列（836 行），方括号 0 行。
--   或尾段门控（RecallAnchor=false）只打「顶层或尾段」与「先天性/遗传性/病理性/异常的」裸属性尾段，
--   逗号尾段不在射程（见/另见 纪律 6 的实测否决）；整形 也不是任何 0 级行的头段键，无跨行锚定噪声。
--
-- ② 部位子行必须挂在 0 级主导词下（索引惯例「主导词 → －部位」），不能挂在 －整形(40003) 下：
--   主导词已由 整形手术 锚住，子行 －整形 对输入再无"新内容"，AliasAddsInputContent 直接拦住
--   （IcdIndexMatcher.cs:441）；Connector 透明化又只对"词面没写进输入"的行生效（同文件:131），
--   整形 明明写在输入里 → 外科手术→整形→躯干 这一跳在匹配器侧永远进不去（实测挂 40003 时 26 名零变化）。
--   词面写成 "－整形，躯干"：逗号切段后 躯干 是独立别名键（0.85），相对路径新增 2 字覆盖 → 入选；
--   同时保留 整形 语境，与打印本「外科手术→整形→部位」的读法一致。
--
-- ③ 码源：6 条与 39996~40002（外科手术→再建→部位）逐码同构，是打印本给这一主导词的同码细分，
--   不是新造编码。指针展开而不改匹配器：ResolveJumpLead 对带逗号的目标只锚首词、不沿子层续钻
--   （M2 已知残留），且目标首词就是本主导词自己（外科手术），改道会撞 visited 自指环（纪律 12 同型）。
--
-- 必须在 icd_index 导入（tools/knowledge/import_icd_index.ps1，按 seq_no upsert）之后执行：
--   upsert 不删行，所以 60001~60006 会留存，但 39972 的词面会被 Excel 源覆盖 → 重导后必须重贴本补丁。
-- 回滚：data/knowledge/icd-index-plastic-site-rollback.sql
SET NOCOUNT ON;
DELETE FROM HospitalAi.dbo.icd_index WHERE seq_no BETWEEN 60001 AND 60006;

UPDATE HospitalAi.dbo.icd_index
SET raw_name = N'外科手术，整形，整形手术',
    index_name = N'外科手术，整形，整形手术',
    full_index_name = N'外科手术，整形，整形手术',
    updated_at = SYSDATETIMEOFFSET()
WHERE seq_no = 39972;

INSERT INTO HospitalAi.dbo.icd_index
    (id, seq_no, raw_name, raw_name_en, index_name, index_code, index_level, parent_seq_no, full_index_name, lead_word_status, created_at, updated_at)
VALUES
    (NEWID(), 60001, N'－整形，躯干 Z42.2',      N'－plastic surgery, trunk Z42.2',            N'－整形，躯干',      N'Z42.2', 1, 39972, N'外科手术$整形，躯干',      0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()),
    (NEWID(), 60002, N'－整形，乳房 Z42.1',      N'－plastic surgery, breast Z42.1',           N'－整形，乳房',      N'Z42.1', 1, 39972, N'外科手术$整形，乳房',      0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()),
    (NEWID(), 60003, N'－整形，上肢 Z42.3',      N'－plastic surgery, upper limb Z42.3',       N'－整形，上肢',      N'Z42.3', 1, 39972, N'外科手术$整形，上肢',      0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()),
    (NEWID(), 60004, N'－整形，下肢 Z42.4',      N'－plastic surgery, lower limb Z42.4',       N'－整形，下肢',      N'Z42.4', 1, 39972, N'外科手术$整形，下肢',      0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()),
    (NEWID(), 60005, N'－整形，头和颈 Z42.0',    N'－plastic surgery, head and neck Z42.0',    N'－整形，头和颈',    N'Z42.0', 1, 39972, N'外科手术$整形，头和颈',    0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()),
    (NEWID(), 60006, N'－整形，特指的 NEC Z42.8', N'－plastic surgery, specified NEC Z42.8',   N'－整形，特指的 NEC', N'Z42.8', 1, 39972, N'外科手术$整形，特指的 NEC', 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());

SELECT COUNT(*) AS patched_rows FROM HospitalAi.dbo.icd_index WHERE seq_no BETWEEN 60001 AND 60006;
