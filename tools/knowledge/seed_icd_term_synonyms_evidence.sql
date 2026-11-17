SET NOCOUNT ON;
-- 证据极性同义词种子（诊断通道）——把 P0-P3 极性族的知识数据落到索引链路词库。
-- 行集与知识库权威文件 data/knowledge/coding-rules-2026.json 的 synonyms 段
-- （entityType=DIAGNOSIS、normalizedTerm 命中 证实/确定的/被…分支 词面的 13 行）逐条同源：
--   * 证实 ≡ 确定的 ≡ (+)              （A15.0 家族 阳性分支 18847 确定的）
--   * 显微镜下痰检查 ≡ 痰镜检 ≡ 痰涂片  （18849 被显微镜下痰检查）
--   * 确定的被显微镜下痰检查 ≡ 痰涂片(+) ≡ 痰镜检(+) ≡ 涂阳
--   * 确定的仅被培养 ≡ 痰培养(+) ≡ 涂阴培阳 ≡ 培阳   （18851 仅被培养，A15.1）
--   * 确定的被组织学 ≡ 病理(+) ≡ 病理检查(+) ≡ 组织学(+)（18850 被组织学，A15.2）
--   * 被未特指的方法证实 ≡ 经证实（独立组，不桥接 证实≡确定的 组）：
--     索引分支 被未特指的方法（18848，A15.3），字典真码族全写 经证实(+)（A15.300-X011）。
--     独立成组是刻意的：若挂到 证实≡确定的 组下，经证实 会桥接两组合并，
--     病理(+) 等输入的证据组成员被污染（案例3 用户方案）。
--   * 确定的被组织学 ≡ 伴有细菌学和组织学证实：非肺部位树（鼻/鼻中隔/鼻咽/喉气管支气管/
--     支气管/…）的"已证实"分支词面是这一条（19238 支气管→伴有细菌学和组织学证实 A15.5、
--     18800 鼻(中隔)→… A15.8），字典真码（A15.509 支气管结核,病理(+) 等）就挂在这些分支下——
--     病理(+) 不与分支词面同组，变体改写够不到分支词面，非肺部位的证据链整族走不通
--     （CAP 万条 2 条族级回归的根因）。病理(+)（组织学证实）映射到"细菌学和组织学证实"
--     分支与字典自身组织一致：A15.509/A15.802/A15.804 等 病理(+) 行全部挂在该分支下。
-- 等价类按连通分量合并（IcdTermSynonymSnapshot.Build），类内任意两词互换改写。
-- 阴性词（(-)/阴性/涂阴[单独]）不入表：语义上对应 A16.0 检查为阴性，挂到"证实/确定的"
-- 会降级误码——阴性输入走 R2 极性裁决留在未证实分支。
-- 纪律：本种子只做增量 MERGE（幂等），**不带全量停用**——icd_term_synonym 现存 980+ 行
-- 由知识导入（KnowledgeEndpoints → SqlServerCodingKnowledgeService）与人工维护，
-- seed_icd_term_synonyms.sql 的"未列出即停用"口径不适用于本文件。
-- 验收：IcdIndexDiag 逐名——肺结核瘤,病理(+)→A15.201、结核性肺纤维变性,病理(+)→A15.203、
-- 继发性肺结核(初治,耐多药)涂阴培阳→A15.100X005、肺结核瘤,痰镜检(+)→A15.0 族、
-- 痰镜检(-) 不得出 A15；万条回归（logs/names_10000.txt）对种子前台账 diff 审计。
MERGE icd_term_synonym AS t
USING (VALUES
    (N'DIAGNOSIS', N'确定的',               N'证实'),
    (N'DIAGNOSIS', N'证实',                 N'(+)'),
    (N'DIAGNOSIS', N'显微镜下痰检查',        N'痰涂片'),
    (N'DIAGNOSIS', N'显微镜下痰检查',        N'痰镜检'),
    (N'DIAGNOSIS', N'确定的被显微镜下痰检查', N'痰涂片(+)'),
    (N'DIAGNOSIS', N'确定的被显微镜下痰检查', N'痰镜检(+)'),
    (N'DIAGNOSIS', N'确定的被显微镜下痰检查', N'涂阳'),
    (N'DIAGNOSIS', N'确定的仅被培养',        N'痰培养(+)'),
    (N'DIAGNOSIS', N'确定的仅被培养',        N'涂阴培阳'),
    (N'DIAGNOSIS', N'确定的仅被培养',        N'培阳'),
    (N'DIAGNOSIS', N'确定的被组织学',        N'病理(+)'),
    (N'DIAGNOSIS', N'确定的被组织学',        N'病理检查(+)'),
    (N'DIAGNOSIS', N'确定的被组织学',        N'组织学(+)'),
    (N'DIAGNOSIS', N'确定的被组织学',        N'伴有细菌学和组织学证实'),
    (N'DIAGNOSIS', N'被未特指的方法证实',     N'经证实')
) AS s(term_type, canonical_term, variant_term)
ON t.term_type = s.term_type AND t.variant_term = s.variant_term
WHEN MATCHED THEN UPDATE SET
    canonical_term = s.canonical_term,
    is_enabled = 1,
    updated_at = SYSDATETIMEOFFSET()
WHEN NOT MATCHED THEN INSERT
    (id, term_type, canonical_term, variant_term, is_enabled, created_at, updated_at)
VALUES
    (NEWID(), s.term_type, s.canonical_term, s.variant_term, 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());

-- 同一词面若在历史行里挂过别的规范词（方向配反/旧口径），停用旧行保新口径：
-- 等价类合并只认 is_enabled=1 的行，旧行残留会把类标签拉偏。
UPDATE old_row
SET old_row.is_enabled = 0, old_row.updated_at = SYSDATETIMEOFFSET()
FROM icd_term_synonym AS old_row
WHERE old_row.term_type = N'DIAGNOSIS'
  AND old_row.is_enabled = 1
  AND EXISTS (
      SELECT 1 FROM (VALUES
        (N'确定的', N'证实'), (N'证实', N'(+)'),
        (N'显微镜下痰检查', N'痰涂片'), (N'显微镜下痰检查', N'痰镜检'),
        (N'确定的被显微镜下痰检查', N'痰涂片(+)'), (N'确定的被显微镜下痰检查', N'痰镜检(+)'),
        (N'确定的被显微镜下痰检查', N'涂阳'),
        (N'确定的仅被培养', N'痰培养(+)'), (N'确定的仅被培养', N'涂阴培阳'), (N'确定的仅被培养', N'培阳'),
        (N'确定的被组织学', N'病理(+)'), (N'确定的被组织学', N'病理检查(+)'), (N'确定的被组织学', N'组织学(+)'),
        (N'确定的被组织学', N'伴有细菌学和组织学证实'),
        (N'被未特指的方法证实', N'经证实')
    ) AS keep(canonical_term, variant_term)
      WHERE keep.variant_term = old_row.variant_term
        AND keep.canonical_term <> old_row.canonical_term);

SELECT term_type, canonical_term, variant_term
FROM icd_term_synonym
WHERE is_enabled = 1
  AND (canonical_term LIKE N'%确定的%' OR variant_term IN (N'证实', N'(+)'))
ORDER BY canonical_term, variant_term;
