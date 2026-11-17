SET NOCOUNT ON;
-- E3 式知识补丁：结核树 声门 行补 声带 别名（案例4，2026-11-17 用户口径）。
-- 背景：索引书结核(病) 树下只有 －声门(19130→伴有细菌学和组织学证实 A15.5)，
-- 无 －声带 行；字典真码 声带结核(细菌学和组织学证实) A15.500X004 挂在该分支下。
-- 临床书写 声带结核 时主导词树找不到部位行，链路掉进 结缔组织 A18.8 噪声分支。
-- 声带=声门 在 ICD-10 解剖学同区（喉部声门区），但 J38 树两者是不同节点不同码
-- （声带 J38.3 / 声门 J38.0/J38.5），故不做全局同义词（icd_term_synonym），
-- 只在结核树该行补逗号别名——逗号段产生独立别名，只影响结核树下钻，J38 树零波及。
-- 回滚：UPDATE icd_index SET index_name=N'－声门' WHERE seq_no=19130;
UPDATE icd_index
SET index_name = N'－声门，声带', updated_at = SYSDATETIMEOFFSET()
WHERE seq_no = 19130
  AND index_name = N'－声门';

SELECT seq_no, index_name, index_code FROM icd_index WHERE seq_no = 19130;
