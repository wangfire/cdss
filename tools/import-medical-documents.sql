-- ============================================================================
-- Medical Document Import Script (v2)
-- Create medical_document records with inline clinical text from source DB.
-- ============================================================================

USE HospitalAi;
SET NOCOUNT ON;

DECLARE @HospitalId UNIQUEIDENTIFIER = '868F2319-9A0C-46C8-B478-C78FB350237D';
DECLARE @Now DATETIMEOFFSET = SYSDATETIMEOFFSET();
DECLARE @Count INT;

-- ============================================================================
-- Step 1: Pick one SeqNo per visit (latest by SeqNo)
-- ============================================================================
IF OBJECT_ID('tempdb..#VisitSeqNo') IS NOT NULL DROP TABLE #VisitSeqNo;
SELECT v.id AS VisitId, src.SeqNo, src.BingAH
INTO #VisitSeqNo
FROM visit v
INNER JOIN patient p ON v.patient_id = p.id
INNER JOIN WaterCloudNetDb_TongLiao.dbo.BingRenZhuYuanXinXi src
    ON src.BingAH = p.source_patient_id
WHERE v.hospital_id = @HospitalId
    AND src.SeqNo = (
        SELECT MAX(s2.SeqNo)
        FROM WaterCloudNetDb_TongLiao.dbo.BingRenZhuYuanXinXi s2
        WHERE s2.BingAH = p.source_patient_id
    );

SELECT @Count = COUNT(*) FROM #VisitSeqNo;
PRINT 'Visits mapped to SeqNo: ' + CAST(@Count AS NVARCHAR);

-- ============================================================================
-- Step 2: Build diagnosis text per SeqNo
-- ============================================================================
IF OBJECT_ID('tempdb..#DiagText') IS NOT NULL DROP TABLE #DiagText;
SELECT d.SeqNo,
    STRING_AGG(
        ISNULL(d.LinChZhD, N'') + N'（' + ISNULL(d.D_ICD_ICD_10, N'未知') + N'）',
        N'；'
    ) AS diag_text
INTO #DiagText
FROM WaterCloudNetDb_TongLiao.dbo.BingRenZhenDuanXinXi d
INNER JOIN #VisitSeqNo vs ON d.SeqNo = vs.SeqNo
WHERE d.LinChZhD IS NOT NULL AND LEN(d.LinChZhD) > 0
GROUP BY d.SeqNo;

SELECT @Count = COUNT(*) FROM #DiagText;
PRINT 'SeqNo with diagnoses: ' + CAST(@Count AS NVARCHAR);

-- ============================================================================
-- Step 3: Build surgery text per SeqNo
-- ============================================================================
IF OBJECT_ID('tempdb..#SurgText') IS NOT NULL DROP TABLE #SurgText;
SELECT s.SeqNo,
    STRING_AGG(
        ISNULL(s.LinChShShMCh, N'') + N'（' + ISNULL(s.D_CM3_Code, N'未知') + N'）',
        N'；'
    ) AS surg_text
INTO #SurgText
FROM WaterCloudNetDb_TongLiao.dbo.BingRenShouShuXinXi s
INNER JOIN #VisitSeqNo vs ON s.SeqNo = vs.SeqNo
WHERE s.LinChShShMCh IS NOT NULL AND LEN(s.LinChShShMCh) > 0
GROUP BY s.SeqNo;

SELECT @Count = COUNT(*) FROM #SurgText;
PRINT 'SeqNo with surgeries: ' + CAST(@Count AS NVARCHAR);

-- ============================================================================
-- Step 4: Clear existing medical documents and sections
-- ============================================================================
DELETE FROM document_section
WHERE medical_document_id IN (
    SELECT id FROM medical_document WHERE hospital_id = @HospitalId
);
DELETE FROM medical_document WHERE hospital_id = @HospitalId;
PRINT 'Cleared existing medical documents.';

-- ============================================================================
-- Step 5: Insert medical_document records with inline clinical text
-- ============================================================================
INSERT INTO medical_document (
    id, hospital_id, visit_id, document_type, content_reference,
    content_hash, version, created_at, updated_at
)
SELECT
    NEWID(),
    @HospitalId,
    vs.VisitId,
    'ADMISSION_NOTE',
    N'入院记录：'
    + CASE WHEN dt.diag_text IS NOT NULL THEN N'诊断：' + dt.diag_text + N'。' ELSE N'' END
    + CASE WHEN st.surg_text IS NOT NULL THEN N'手术操作：' + st.surg_text + N'。' ELSE N'' END,
    CONVERT(NVARCHAR(128), HASHBYTES('SHA2_256',
        ISNULL(dt.diag_text, N'') + ISNULL(st.surg_text, N'')
    ), 2),
    1,
    @Now,
    @Now
FROM #VisitSeqNo vs
LEFT JOIN #DiagText dt ON vs.SeqNo = dt.SeqNo
LEFT JOIN #SurgText st ON vs.SeqNo = st.SeqNo
WHERE dt.diag_text IS NOT NULL OR st.surg_text IS NOT NULL;

SELECT @Count = COUNT(*) FROM medical_document WHERE hospital_id = @HospitalId;
PRINT 'Medical documents created: ' + CAST(@Count AS NVARCHAR);

-- ============================================================================
-- Step 6: Verify
-- ============================================================================
PRINT '';
PRINT '=== Sample content (first 3) ===';
SELECT TOP 3
    LEFT(content_reference, 200) AS Preview,
    LEN(content_reference) AS Len
FROM medical_document
WHERE hospital_id = @HospitalId
ORDER BY created_at;

-- ============================================================================
-- Cleanup
-- ============================================================================
DROP TABLE #VisitSeqNo;
DROP TABLE #DiagText;
DROP TABLE #SurgText;

PRINT '';
PRINT '========================================';
PRINT 'Medical document import completed!';
PRINT '========================================';
