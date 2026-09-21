SET XACT_ABORT ON;
BEGIN TRAN;

DECLARE @hid uniqueidentifier = '868F2319-9A0C-46C8-B478-C78FB350237D';

IF OBJECT_ID('tempdb..#pats') IS NOT NULL DROP TABLE #pats;
IF OBJECT_ID('tempdb..#visits') IS NOT NULL DROP TABLE #visits;
IF OBJECT_ID('tempdb..#tasks') IS NOT NULL DROP TABLE #tasks;
IF OBJECT_ID('tempdb..#recs') IS NOT NULL DROP TABLE #recs;
IF OBJECT_ID('tempdb..#docs') IS NOT NULL DROP TABLE #docs;
IF OBJECT_ID('tempdb..#traces') IS NOT NULL DROP TABLE #traces;

SELECT id INTO #pats FROM patient WHERE hospital_id=@hid AND source_patient_id IN ('000001','000002','000003','TEST999001','TEST999002');
SELECT v.id INTO #visits FROM visit v JOIN #pats p ON p.id=v.patient_id;
SELECT t.id INTO #tasks FROM coding_task t JOIN #visits v ON v.id=t.visit_id;
SELECT r.id INTO #recs FROM coding_recommendation r JOIN #tasks t ON t.id=r.coding_task_id;
SELECT d.id INTO #docs FROM medical_document d JOIN #visits v ON v.id=d.visit_id;
SELECT tr.id INTO #traces FROM pipeline_trace tr JOIN #tasks t ON t.id=tr.coding_task_id;

DELETE FROM recommendation_score WHERE recommendation_id IN (SELECT id FROM #recs);
DELETE FROM recommendation_evidence WHERE coding_recommendation_id IN (SELECT id FROM #recs);
DELETE FROM coding_recommendation WHERE coding_task_id IN (SELECT id FROM #tasks);
DELETE FROM clinical_fact_evidence WHERE fact_id IN (SELECT id FROM clinical_fact WHERE coding_task_id IN (SELECT id FROM #tasks));
DELETE FROM clinical_fact WHERE coding_task_id IN (SELECT id FROM #tasks);
DELETE FROM clinical_entity WHERE coding_task_id IN (SELECT id FROM #tasks);
DELETE FROM clinical_evidence WHERE coding_task_id IN (SELECT id FROM #tasks);
DELETE FROM coding_diagnosis_input WHERE coding_task_id IN (SELECT id FROM #tasks);
DELETE FROM coding_review WHERE coding_task_id IN (SELECT id FROM #tasks);
DELETE FROM final_coding_result WHERE coding_task_id IN (SELECT id FROM #tasks);
DELETE FROM quality_issue WHERE coding_task_id IN (SELECT id FROM #tasks);
DELETE FROM pipeline_trace_step WHERE pipeline_trace_id IN (SELECT id FROM #traces);
DELETE FROM pipeline_trace WHERE id IN (SELECT id FROM #traces);
DELETE FROM document_section WHERE medical_document_id IN (SELECT id FROM #docs);
DELETE FROM medical_document WHERE id IN (SELECT id FROM #docs);
DELETE FROM coding_task WHERE id IN (SELECT id FROM #tasks);
DELETE FROM visit WHERE id IN (SELECT id FROM #visits);
DELETE FROM patient WHERE id IN (SELECT id FROM #pats);

COMMIT;
SELECT 'patients_left' AS k, COUNT(*) AS n FROM patient WHERE hospital_id=@hid AND source_patient_id IN ('000001','000002','000003','TEST999001','TEST999002');
