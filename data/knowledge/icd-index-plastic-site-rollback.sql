-- 回滚 data/knowledge/icd-index-plastic-site-patch.sql。
-- 39972 词面还原为 Excel 源原值，60001~60006 部位子行整段删除。
SET NOCOUNT ON;
DELETE FROM HospitalAi.dbo.icd_index WHERE seq_no BETWEEN 60001 AND 60006;

UPDATE HospitalAi.dbo.icd_index
SET raw_name = N'外科手术',
    index_name = N'外科手术',
    full_index_name = N'外科手术',
    updated_at = SYSDATETIMEOFFSET()
WHERE seq_no = 39972;

SELECT seq_no, index_name, ISNULL(index_code, N'') AS index_code
FROM HospitalAi.dbo.icd_index
WHERE seq_no = 39972 OR seq_no BETWEEN 60001 AND 60006
ORDER BY seq_no;
