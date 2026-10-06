SET NOCOUNT ON;
-- 索引链路术语同义词种子（诊断通道）。
-- 口径说明：
--  * 每行 (规范词, 变体词) 是一条"等同边"，快照按连通分量合并成等价类（见 IcdTermSynonymSnapshot），
--    类内任意两词都可互换改写：一个组只需登记一条 变体→规范词 链，不必再两两配对。
--    因此 乳腺=乳房 只保留一行（反向镜像行由本文件末尾清理），
--    早搏=早跳=过早搏动=过早收缩=除极=去极化=期前收缩 全部挂在同一规范词下即成类。
--  * 组标签（canonical_term）决定"全类规范改写"那条检索文本用哪个词，取索引覆盖度最高者；
--    嵌套对（短词是长词的子串）仍要选长词作标签：术/术后 挂在 手术后 下，
--    下钻的"新增输入字符"门控要靠改写补进"手"等字符，标签取 术后 会让 77 个"术后"类诊断名失去推荐。
--  * 1=I=Ⅰ … 5=IIIII=Ⅴ 分度写法：单字符成员不入改写规则（替换锚点过宽会误伤，如 "手术"→"手术后"），
--    等同关系由 TextMatch.NormalizeKey 在归一化键上统一（Unicode 罗码 Ⅰ-Ⅴ 直接转数字，
--    ASCII 罗码串在 度/级/型/期/类 或行尾转数字，最多 4 位——"II-III期" 这类范围写法标点剥离后
--    连成 iiiii，若识别成 5 会把范围误并，故 IIIII 保持原样，只认 Ⅴ/V）。此处按业务口径落表，页面可维护。
--  * "伴→伴有" 索引枢纽词通行规则在 IcdIndexMatcher.AliasAddsInputContent 内实现，不落表。
--  * 每组改动都要过 10000 名逐名 Top-1 diff 回归，见 memory: jzey-10k-diagnosis-test。
MERGE icd_term_synonym AS t
USING (VALUES
    -- 等价类：器官/疾病名
    (N'DIAGNOSIS', N'乳房',           N'乳腺'),
    (N'DIAGNOSIS', N'甲状腺功能亢进',  N'甲亢'),
    (N'DIAGNOSIS', N'期前收缩',       N'早搏'),
    (N'DIAGNOSIS', N'期前收缩',       N'早跳'),
    (N'DIAGNOSIS', N'期前收缩',       N'过早搏动'),
    (N'DIAGNOSIS', N'期前收缩',       N'过早收缩'),
    (N'DIAGNOSIS', N'期前收缩',       N'除极'),
    (N'DIAGNOSIS', N'期前收缩',       N'去极化'),
    (N'DIAGNOSIS', N'手术后',         N'术'),
    (N'DIAGNOSIS', N'手术后',         N'术后'),
    (N'DIAGNOSIS', N'心肌梗死',       N'心梗'),
    (N'DIAGNOSIS', N'心肌梗死',       N'心肌梗塞'),
    (N'DIAGNOSIS', N'胆囊的',         N'胆囊'),
    (N'DIAGNOSIS', N'泌尿',           N'尿路'),
    (N'DIAGNOSIS', N'冠状动脉',       N'冠脉'),
    (N'DIAGNOSIS', N'纤颤',           N'颤动'),
    -- 等价类：分度/分级写法（1-5）
    (N'DIAGNOSIS', N'1',              N'I'),
    (N'DIAGNOSIS', N'1',              N'Ⅰ'),
    (N'DIAGNOSIS', N'2',              N'II'),
    (N'DIAGNOSIS', N'2',              N'Ⅱ'),
    (N'DIAGNOSIS', N'3',              N'III'),
    (N'DIAGNOSIS', N'3',              N'Ⅲ'),
    (N'DIAGNOSIS', N'4',              N'IIII'),
    (N'DIAGNOSIS', N'4',              N'Ⅳ'),
    (N'DIAGNOSIS', N'5',              N'IIIII'),
    (N'DIAGNOSIS', N'5',              N'Ⅴ')
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

-- 上表未列出的历史行（如方向配反的 泌尿→尿路、已废弃的 尿道→泌尿）停用而不删除，保留可追溯的修订痕迹。
UPDATE icd_term_synonym
SET is_enabled = 0, updated_at = SYSDATETIMEOFFSET()
WHERE term_type = N'DIAGNOSIS'
  AND NOT EXISTS (
      SELECT 1 FROM (VALUES
          (N'乳腺', N'乳房'), (N'甲亢', N'甲状腺功能亢进'),
          (N'早搏', N'期前收缩'), (N'早跳', N'期前收缩'), (N'过早搏动', N'期前收缩'),
          (N'过早收缩', N'期前收缩'), (N'除极', N'期前收缩'), (N'去极化', N'期前收缩'),
          (N'术', N'手术后'), (N'术后', N'手术后'),
          (N'心梗', N'心肌梗死'), (N'心肌梗塞', N'心肌梗死'),
          (N'胆囊', N'胆囊的'), (N'尿路', N'泌尿'), (N'冠脉', N'冠状动脉'), (N'颤动', N'纤颤'),
          (N'I', N'1'), (N'Ⅰ', N'1'),
          (N'II', N'2'), (N'Ⅱ', N'2'),
          (N'III', N'3'), (N'Ⅲ', N'3'),
          (N'IIII', N'4'), (N'Ⅳ', N'4'),
          (N'IIIII', N'5'), (N'Ⅴ', N'5')
      ) AS keep(variant_term, canonical_term)
      WHERE keep.variant_term = icd_term_synonym.variant_term
        AND keep.canonical_term = icd_term_synonym.canonical_term
  );

-- 等价类按连通分量合并后，同一对词的反向镜像边没有任何额外信息量，却会在维护页裂成两行、
-- 看起来仍像"一对一"映射；这类已停用的镜像行直接删除（只删停用行，不动人工启用的数据）。
DELETE FROM icd_term_synonym
WHERE icd_term_synonym.is_enabled = 0
  AND EXISTS (
      SELECT 1 FROM icd_term_synonym AS m
      WHERE m.term_type = icd_term_synonym.term_type
        AND m.is_enabled = 1
        AND m.variant_term = icd_term_synonym.canonical_term
        AND m.canonical_term = icd_term_synonym.variant_term
  );

SELECT term_type, canonical_term, COUNT(*) AS variant_count
FROM icd_term_synonym
WHERE is_enabled = 1
GROUP BY term_type, canonical_term
ORDER BY term_type, canonical_term;
