-- ============================================================================
-- Medical Records Test Data Migration Script
-- Extract 1000 records from WaterCloudNetDb_TongLiao
-- Insert into HospitalAi as patients, visits, coding tasks, recommendations
-- ============================================================================

USE HospitalAi;
SET NOCOUNT ON;

DECLARE @HospitalId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';
DECLARE @Now DATETIMEOFFSET = SYSDATETIMEOFFSET();
DECLARE @BatchSize INT = 1000;
DECLARE @Count INT;

-- ============================================================================
-- Step 1: Extract source data into temp tables
-- ============================================================================
IF OBJECT_ID('tempdb..#SourceVisits') IS NOT NULL DROP TABLE #SourceVisits;
IF OBJECT_ID('tempdb..#SourcePatients') IS NOT NULL DROP TABLE #SourcePatients;
IF OBJECT_ID('tempdb..#SourceDiagnoses') IS NOT NULL DROP TABLE #SourceDiagnoses;
IF OBJECT_ID('tempdb..#SourceSurgeries') IS NOT NULL DROP TABLE #SourceSurgeries;

SELECT TOP (@BatchSize)
    m.SeqNo,
    m.BingAH,
    m.NianL AS Age,
    m.RuYShJ AS AdmissionAt,
    m.ChuYShJ AS DischargeAt,
    m.RuYKB AS AdmissionDept,
    m.ChuYKB AS DischargeDept
INTO #SourceVisits
FROM WaterCloudNetDb_TongLiao.dbo.BingRenZhuYuanXinXi m
WHERE m.RuYShJ IS NOT NULL
ORDER BY m.SeqNo DESC;

SELECT DISTINCT
    sv.BingAH,
    b.XingM AS DisplayName,
    b.XingB AS Gender,
    b.ChuShRQ AS BirthDate
INTO #SourcePatients
FROM #SourceVisits sv
INNER JOIN WaterCloudNetDb_TongLiao.dbo.BingRenZhuYuanJiBenXinXi b ON sv.SeqNo = b.SeqNo;

SELECT
    sv.SeqNo,
    sv.BingAH,
    d.LinChZhD AS DiagnosisName,
    d.D_ICD_ICD_10 AS Icd10Code,
    d.D_ICD_Name AS Icd10Name,
    d.ShunX AS SortOrder
INTO #SourceDiagnoses
FROM #SourceVisits sv
INNER JOIN WaterCloudNetDb_TongLiao.dbo.BingRenZhenDuanXinXi d ON sv.SeqNo = d.SeqNo
WHERE d.LinChZhD IS NOT NULL AND LEN(d.LinChZhD) > 0;

SELECT
    sv.SeqNo,
    sv.BingAH,
    s.LinChShShMCh AS SurgeryName,
    s.D_CM3_Code AS Icd9cm3Code,
    s.D_CM3_Name AS Icd9cm3Name,
    s.ShunX AS SortOrder
INTO #SourceSurgeries
FROM #SourceVisits sv
INNER JOIN WaterCloudNetDb_TongLiao.dbo.BingRenShouShuXinXi s ON sv.SeqNo = s.SeqNo
WHERE s.LinChShShMCh IS NOT NULL AND LEN(s.LinChShShMCh) > 0;

SELECT @Count = COUNT(*) FROM #SourceVisits; PRINT 'Source visits: ' + CAST(@Count AS NVARCHAR);
SELECT @Count = COUNT(*) FROM #SourcePatients; PRINT 'Source patients: ' + CAST(@Count AS NVARCHAR);
SELECT @Count = COUNT(*) FROM #SourceDiagnoses; PRINT 'Source diagnoses: ' + CAST(@Count AS NVARCHAR);
SELECT @Count = COUNT(*) FROM #SourceSurgeries; PRINT 'Source surgeries: ' + CAST(@Count AS NVARCHAR);

-- ============================================================================
-- Step 2: Create patient records
-- ============================================================================
IF OBJECT_ID('tempdb..#PatientMapping') IS NOT NULL DROP TABLE #PatientMapping;
CREATE TABLE #PatientMapping (
    BingAH NVARCHAR(50),
    PatientId UNIQUEIDENTIFIER
);

INSERT INTO #PatientMapping (BingAH, PatientId)
SELECT BingAH, NEWID() FROM #SourcePatients;

INSERT INTO patient (id, hospital_id, source_system, source_patient_id, display_name, created_at, updated_at)
SELECT pm.PatientId, @HospitalId, 'WaterCloudNetDb_TongLiao', sp.BingAH, sp.DisplayName, @Now, @Now
FROM #SourcePatients sp
INNER JOIN #PatientMapping pm ON sp.BingAH = pm.BingAH;

SELECT @Count = COUNT(*) FROM #PatientMapping; PRINT 'Patients created: ' + CAST(@Count AS NVARCHAR);

-- ============================================================================
-- Step 3: Create visit records
-- ============================================================================
IF OBJECT_ID('tempdb..#VisitMapping') IS NOT NULL DROP TABLE #VisitMapping;
CREATE TABLE #VisitMapping (
    SeqNo NVARCHAR(255),
    VisitId UNIQUEIDENTIFIER
);

INSERT INTO #VisitMapping (SeqNo, VisitId)
SELECT SeqNo, NEWID() FROM #SourceVisits;

INSERT INTO visit (id, hospital_id, patient_id, admission_at, discharge_at, created_at, updated_at)
SELECT vm.VisitId, @HospitalId, pm.PatientId, sv.AdmissionAt, sv.DischargeAt, @Now, @Now
FROM #SourceVisits sv
INNER JOIN #VisitMapping vm ON sv.SeqNo = vm.SeqNo
INNER JOIN #PatientMapping pm ON sv.BingAH = pm.BingAH;

SELECT @Count = COUNT(*) FROM #VisitMapping; PRINT 'Visits created: ' + CAST(@Count AS NVARCHAR);

-- ============================================================================
-- Step 4: Create coding tasks
-- ============================================================================
IF OBJECT_ID('tempdb..#TaskMapping') IS NOT NULL DROP TABLE #TaskMapping;
CREATE TABLE #TaskMapping (
    SeqNo NVARCHAR(255),
    TaskId UNIQUEIDENTIFIER
);

INSERT INTO #TaskMapping (SeqNo, TaskId)
SELECT SeqNo, NEWID() FROM #VisitMapping;

INSERT INTO coding_task (id, hospital_id, visit_id, pipeline_version, status, retry_count, started_at, completed_at, created_at, updated_at, idempotency_key)
SELECT tm.TaskId, @HospitalId, vm.VisitId, 'v1.0-migration', 8, 0, @Now, @Now, @Now, @Now, 'MIGRATION-' + vm.SeqNo
FROM #VisitMapping vm
INNER JOIN #TaskMapping tm ON vm.SeqNo = tm.SeqNo;

SELECT @Count = COUNT(*) FROM #TaskMapping; PRINT 'Coding tasks created: ' + CAST(@Count AS NVARCHAR);

-- ============================================================================
-- Step 5: Create diagnosis coding recommendations (ICD-10)
-- ============================================================================
IF OBJECT_ID('tempdb..#DiagRecs') IS NOT NULL DROP TABLE #DiagRecs;
CREATE TABLE #DiagRecs (
    Id UNIQUEIDENTIFIER,
    TaskId UNIQUEIDENTIFIER,
    Code NVARCHAR(100),
    Title NVARCHAR(500),
    SortOrder INT
);

INSERT INTO #DiagRecs (Id, TaskId, Code, Title, SortOrder)
SELECT NEWID(), tm.TaskId,
    COALESCE(NULLIF(sd.Icd10Code, ''), 'UNKNOWN'),
    COALESCE(NULLIF(sd.DiagnosisName, ''), NULLIF(sd.Icd10Name, ''), 'Unknown Diagnosis'),
    COALESCE(sd.SortOrder, 1)
FROM #SourceDiagnoses sd
INNER JOIN #TaskMapping tm ON sd.SeqNo = tm.SeqNo;

INSERT INTO coding_recommendation (id, hospital_id, coding_task_id, recommendation_type, code_system_code, code, title, rank, recall_score, rule_score, confidence_score, review_status, created_at, updated_at)
SELECT dr.Id, @HospitalId, dr.TaskId, 'DIAGNOSIS', 'ICD-10', dr.Code, dr.Title, dr.SortOrder, 0.85, 0.90, 0.88, 'PENDING', @Now, @Now
FROM #DiagRecs dr;

SELECT @Count = COUNT(*) FROM #DiagRecs; PRINT 'Diagnosis recommendations created: ' + CAST(@Count AS NVARCHAR);

-- ============================================================================
-- Step 6: Create surgery coding recommendations (ICD-9-CM-3)
-- ============================================================================
IF OBJECT_ID('tempdb..#SurgRecs') IS NOT NULL DROP TABLE #SurgRecs;
CREATE TABLE #SurgRecs (
    Id UNIQUEIDENTIFIER,
    TaskId UNIQUEIDENTIFIER,
    Code NVARCHAR(100),
    Title NVARCHAR(500),
    SortOrder INT
);

INSERT INTO #SurgRecs (Id, TaskId, Code, Title, SortOrder)
SELECT NEWID(), tm.TaskId,
    COALESCE(NULLIF(ss.Icd9cm3Code, ''), 'UNKNOWN'),
    COALESCE(NULLIF(ss.SurgeryName, ''), NULLIF(ss.Icd9cm3Name, ''), 'Unknown Procedure'),
    COALESCE(ss.SortOrder, 1)
FROM #SourceSurgeries ss
INNER JOIN #TaskMapping tm ON ss.SeqNo = tm.SeqNo;

INSERT INTO coding_recommendation (id, hospital_id, coding_task_id, recommendation_type, code_system_code, code, title, rank, recall_score, rule_score, confidence_score, review_status, created_at, updated_at)
SELECT sr.Id, @HospitalId, sr.TaskId, 'PROCEDURE', 'ICD-9-CM-3', sr.Code, sr.Title, sr.SortOrder, 0.85, 0.90, 0.88, 'PENDING', @Now, @Now
FROM #SurgRecs sr;

SELECT @Count = COUNT(*) FROM #SurgRecs; PRINT 'Surgery recommendations created: ' + CAST(@Count AS NVARCHAR);

-- ============================================================================
-- Step 7: Cleanup
-- ============================================================================
DROP TABLE #SourceVisits;
DROP TABLE #SourcePatients;
DROP TABLE #SourceDiagnoses;
DROP TABLE #SourceSurgeries;
DROP TABLE #PatientMapping;
DROP TABLE #VisitMapping;
DROP TABLE #TaskMapping;
DROP TABLE #DiagRecs;
DROP TABLE #SurgRecs;

PRINT '';
PRINT '========================================';
PRINT 'Migration completed!';
PRINT '========================================';

-- Verification
SELECT 'patient' AS EntityType, COUNT(*) AS Count FROM patient WHERE hospital_id = @HospitalId;
SELECT 'visit' AS EntityType, COUNT(*) AS Count FROM visit WHERE hospital_id = @HospitalId;
SELECT 'coding_task' AS EntityType, COUNT(*) AS Count FROM coding_task WHERE hospital_id = @HospitalId;
SELECT 'coding_recommendation_DIAGNOSIS' AS EntityType, COUNT(*) AS Count FROM coding_recommendation WHERE hospital_id = @HospitalId AND recommendation_type = 'DIAGNOSIS';
SELECT 'coding_recommendation_PROCEDURE' AS EntityType, COUNT(*) AS Count FROM coding_recommendation WHERE hospital_id = @HospitalId AND recommendation_type = 'PROCEDURE';
