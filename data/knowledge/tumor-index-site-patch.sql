-- tumor_index 部位词面补丁（D 桶，14 行）。生成: tools/knowledge/build_tumor_index_site_patch.py
-- 必须在 data/knowledge/tumor-index-seed.sql 之后执行；回滚=删除 seq_no BETWEEN 20001 AND 20014。
SET NOCOUNT ON;
DELETE FROM HospitalAi.dbo.tumor_index WHERE seq_no BETWEEN 20001 AND 20014;
INSERT INTO HospitalAi.dbo.tumor_index (id, seq_no, index_name, index_name_en, index_level, parent_seq_no, full_index_name, code_malignant_primary, code_malignant_secondary, code_in_situ, code_benign, code_uncertain, jump_kind, jump_target, created_at, updated_at) VALUES
(NEWID(), 20001, N'－脊髓', NULL, 1, 1, N'肿瘤，肿瘤性 脊髓', N'C72.0', N'C79.4', NULL, N'D33.4', N'D43.4', 0, NULL, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()),
(NEWID(), 20002, N'－胸髓', NULL, 1, 1, N'肿瘤，肿瘤性 胸髓', N'C72.0', N'C79.4', NULL, N'D33.4', N'D43.4', 0, NULL, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()),
(NEWID(), 20003, N'－泪腺', NULL, 1, 1, N'肿瘤，肿瘤性 泪腺', N'C69.5', N'C79.4', N'D09.2', N'D31.5', N'D48.7', 0, NULL, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()),
(NEWID(), 20004, N'－鼻黏膜', NULL, 1, 1, N'肿瘤，肿瘤性 鼻黏膜', N'C30.0', N'C78.3', N'D02.3', N'D14.0', N'D38.5', 0, NULL, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()),
(NEWID(), 20005, N'－舌会厌褶', NULL, 1, 1, N'肿瘤，肿瘤性 舌会厌褶', N'C10.1', N'C79.8', N'D00.0', N'D10.5', N'D37.0', 0, NULL, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()),
(NEWID(), 20006, N'－硬脑膜外', NULL, 1, 1, N'肿瘤，肿瘤性 硬脑膜外', N'C72.9', N'C79.4', NULL, N'D33.9', N'D43.9', 0, NULL, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()),
(NEWID(), 20007, N'－颈静脉孔', NULL, 1, 1, N'肿瘤，肿瘤性 颈静脉孔', N'C72.9', N'C79.3', NULL, N'D33.7', N'D43.9', 0, NULL, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()),
(NEWID(), 20008, N'－幕下', NULL, 1, 1, N'肿瘤，肿瘤性 幕下', N'C71.7', N'C79.3', NULL, N'D33.1', N'D43.1', 0, NULL, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()),
(NEWID(), 20009, N'－胸椎', NULL, 1, 1, N'肿瘤，肿瘤性 胸椎', N'C41.2', N'C79.5', NULL, N'D16.6', N'D48.0', 0, NULL, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()),
(NEWID(), 20010, N'－颌下', NULL, 1, 1, N'肿瘤，肿瘤性 颌下', N'C76.0', N'C79.8', N'D09.7', N'D10.3', N'D48.7', 0, NULL, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()),
(NEWID(), 20011, N'－胸壁淋巴结', NULL, 1, 1, N'肿瘤，肿瘤性 胸壁淋巴结', NULL, N'C77.3', NULL, N'D36.0', N'D48.7', 0, NULL, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()),
(NEWID(), 20012, N'－翼腭窝', NULL, 1, 1, N'肿瘤，肿瘤性 翼腭窝', N'C76.0', N'C79.8', N'D04.3', N'D10.5', N'D48.7', 0, NULL, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()),
(NEWID(), 20013, N'－腭部', NULL, 1, 1, N'肿瘤，肿瘤性 腭部', N'C76.0', N'C79.8', N'D04.3', N'D36.7', N'D48.7', 0, NULL, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()),
(NEWID(), 20014, N'－脊髓膜', NULL, 1, 1, N'肿瘤，肿瘤性 脊髓膜', N'C70.1', N'C79.4', NULL, N'D32.1', N'D42.1', 0, NULL, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
SELECT COUNT(*) AS patched_rows FROM HospitalAi.dbo.tumor_index WHERE seq_no BETWEEN 20001 AND 20014;