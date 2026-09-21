-- Fix surgery recommendations - deduplicate by (task_id, code)
USE HospitalAi;
SET NOCOUNT ON;

DECLARE @HospitalId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';
DECLARE @Now DATETIMEOFFSET = SYSDATETIMEOFFSET();
DECLARE @Count INT;

-- Get SeqNo from idempotency_key
IF OBJECT_ID('tempdb..#TaskSeqMap') IS NOT NULL DROP TABLE #TaskSeqMap;
CREATE TABLE #TaskSeqMap (
    TaskId UNIQUEIDENTIFIER,
    SeqNo NVARCHAR(255)
);

INSERT INTO #TaskSeqMap (TaskId, SeqNo)
SELECT id, REPLACE(idempotency_key, 'MIGRATION-', '')
FROM coding_task
WHERE hospital_id = @HospitalId AND idempotency_key LIKE 'MIGRATION-%';

SELECT @Count = COUNT(*) FROM #TaskSeqMap;
PRINT 'Task mapping: ' + CAST(@Count AS NVARCHAR);

-- Get surgeries for these SeqNos
IF OBJECT_ID('tempdb..#SourceSurgeries') IS NOT NULL DROP TABLE #SourceSurgeries;
SELECT
    tsm.TaskId,
    COALESCE(NULLIF(s.D_CM3_Code, ''), 'UNKNOWN') AS Code,
    COALESCE(NULLIF(s.LinChShShMCh, ''), NULLIF(s.D_CM3_Name, ''), 'Unknown Procedure') AS Title,
    COALESCE(s.ShunX, 1) AS SortOrder
INTO #SourceSurgeries
FROM #TaskSeqMap tsm
INNER JOIN WaterCloudNetDb_TongLiao.dbo.BingRenShouShuXinXi s ON tsm.SeqNo = s.SeqNo
WHERE s.LinChShShMCh IS NOT NULL AND LEN(s.LinChShShMCh) > 0;

SELECT @Count = COUNT(*) FROM #SourceSurgeries;
PRINT 'Raw surgery records: ' + CAST(@Count AS NVARCHAR);

-- Deduplicate by (TaskId, Code)
IF OBJECT_ID('tempdb..#DedupedSurgRecs') IS NOT NULL DROP TABLE #DedupedSurgRecs;
SELECT TaskId, Code, Title, SortOrder
INTO #DedupedSurgRecs
FROM (
    SELECT TaskId, Code, Title, SortOrder,
        ROW_NUMBER() OVER (PARTITION BY TaskId, Code ORDER BY SortOrder) AS rn
    FROM #SourceSurgeries
) t
WHERE rn = 1;

SELECT @Count = COUNT(*) FROM #DedupedSurgRecs;
PRINT 'Deduped surgery records: ' + CAST(@Count AS NVARCHAR);

-- Insert
INSERT INTO coding_recommendation (id, hospital_id, coding_task_id, recommendation_type, code_system_code, code, title, rank, recall_score, rule_score, confidence_score, review_status, created_at, updated_at)
SELECT NEWID(), @HospitalId, dr.TaskId, 'PROCEDURE', 'ICD-9-CM-3', dr.Code, dr.Title, dr.SortOrder, 0.85, 0.90, 0.88, 'PENDING', @Now, @Now
FROM #DedupedSurgRecs dr;

SELECT @Count = @@ROWCOUNT;
PRINT 'Surgery recommendations inserted: ' + CAST(@Count AS NVARCHAR);

DROP TABLE #TaskSeqMap;
DROP TABLE #SourceSurgeries;
DROP TABLE #DedupedSurgRecs;

PRINT 'Done!';

SELECT 'coding_recommendation_DIAGNOSIS' AS EntityType, COUNT(*) AS Count FROM coding_recommendation WHERE hospital_id = @HospitalId AND recommendation_type = 'DIAGNOSIS';
SELECT 'coding_recommendation_PROCEDURE' AS EntityType, COUNT(*) AS Count FROM coding_recommendation WHERE hospital_id = @HospitalId AND recommendation_type = 'PROCEDURE';
