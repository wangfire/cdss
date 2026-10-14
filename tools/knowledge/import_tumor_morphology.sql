-- 肿瘤形态学编码（ICD-O M#####/d）导入 HospitalAi 诊断字典 medical_code。
-- 来源：WaterCloudNetDb_TongLiao.dbo.D_ICD（同实例，跨库 SELECT）。
-- 双份写入沿用现有 ICD-10 字典的两种 hospital_id/code_system_id 配对，
-- 保证 EfIcdDictionarySource 的两条归属路径（hospital_id 或 code_system 归属）都能取到。
-- 幂等：按 (hospital_id, code_system_id, code) NOT EXISTS 防重。
SET NOCOUNT ON;

DECLARE @targets TABLE (hospital_id uniqueidentifier NOT NULL, code_system_id uniqueidentifier NOT NULL);
INSERT INTO @targets (hospital_id, code_system_id) VALUES
    ('868F2319-9A0C-46C8-B478-C78FB350237D', 'B21BC7ED-7783-43B1-BDAA-FADF702C50E2'), -- Demo 医院 × KNOWLEDGE-IMPORT ICD-10
    ('5074310C-FE81-4C68-A810-1382699ED5DB', '18275306-AAB8-44E8-B4B7-E558972B70E8'); -- ICD10CAP 医院 × ICD10CAP ICD-10

INSERT INTO HospitalAi.dbo.medical_code
    (id, hospital_id, code_system_id, code_system_code, code, title, code_type, search_text, is_enabled, created_at, updated_at)
SELECT NEWID(), t.hospital_id, t.code_system_id, 'ICD-10',
       LTRIM(RTRIM(d.ICD_10)), LTRIM(RTRIM(d.Name)), '诊断', LTRIM(RTRIM(d.Name)), 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()
FROM @targets AS t
CROSS JOIN (
    SELECT ICD_10, Name
    FROM WaterCloudNetDb_TongLiao.dbo.D_ICD
    WHERE ICD_10 LIKE 'M%' AND ICD_10 LIKE '%/%' AND ISNULL(Deleted, 0) = 0
    GROUP BY ICD_10, Name
) AS d
WHERE NOT EXISTS (
    SELECT 1 FROM HospitalAi.dbo.medical_code m
    WHERE m.hospital_id = t.hospital_id
      AND m.code_system_id = t.code_system_id
      AND CAST(m.code AS varbinary(64)) = CAST(LTRIM(RTRIM(d.ICD_10)) AS varbinary(64))
);

-- 校验：两份应各 1427 行
SELECT hospital_id, COUNT(*) AS morphology_rows
FROM HospitalAi.dbo.medical_code
WHERE code LIKE 'M%' AND code LIKE '%/%' AND code NOT LIKE '%.%'
GROUP BY hospital_id;
