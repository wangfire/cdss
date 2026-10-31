-- 2026-10-31 对比修复：找回 1420 版知识基线的仓库外增量词面行（27 名流失 Top1 的主导词行）。
-- 来源：1420 版工作簿「主导词修饰词明细」链路记录（LocalDB 原库物理文件已丢，按台账重建）。
SET NOCOUNT ON;
INSERT INTO icd_index (seq_no, raw_name, raw_name_en, index_name, index_code, index_level, parent_seq_no, full_index_name, lead_word_status) VALUES (60007, N'综合征', NULL, N'综合征', N'1', 0, 0, N'综合征', 0);
INSERT INTO icd_index (seq_no, raw_name, raw_name_en, index_name, index_code, index_level, parent_seq_no, full_index_name, lead_word_status) VALUES (60008, N'综合征', NULL, N'综合征', N'无', 0, 0, N'综合征', 0);
INSERT INTO icd_index (seq_no, raw_name, raw_name_en, index_name, index_code, index_level, parent_seq_no, full_index_name, lead_word_status) VALUES (60009, N'先天性', NULL, N'先天性', N'无', 0, 0, N'先天性', 0);
-- 补丁行数: 3