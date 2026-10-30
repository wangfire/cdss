SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
-- 验证样本数据：1 家医院、1 患者、1 就诊、1 任务、6 条 FRONT_PAGE 输入 + rank1 推荐 + 各 2 条证据
DECLARE @hospital_id uniqueidentifier = '11111111-1111-1111-1111-111111111111';
DECLARE @now datetimeoffset(7) = SYSUTCDATETIME();

IF NOT EXISTS (SELECT 1 FROM hospital WHERE id = @hospital_id)
    INSERT INTO hospital (id, code, name, status, created_at, updated_at)
    VALUES (@hospital_id, N'VAL-01', N'验证医院', N'ACTIVE', @now, @now);

IF NOT EXISTS (SELECT 1 FROM patient WHERE hospital_id = @hospital_id AND source_patient_id = N'V-PAT-1')
BEGIN
    DECLARE @patient_id uniqueidentifier = (SELECT TOP 1 id FROM patient WHERE hospital_id = @hospital_id AND source_patient_id = N'V-PAT-1');
    DECLARE @visit_id uniqueidentifier = (SELECT TOP 1 id FROM visit);
    DECLARE @task_id uniqueidentifier = (SELECT TOP 1 id FROM coding_task);

    INSERT INTO patient (id, hospital_id, source_system, source_patient_id, display_name, created_at, updated_at)
    VALUES (@patient_id, @hospital_id, N'VALIDATION', N'V-PAT-1', N'验证患者一', @now, @now);
    INSERT INTO visit (id, hospital_id, patient_id, admission_at, created_at, updated_at)
    VALUES (@visit_id, @hospital_id, @patient_id, @now, @now, @now);
    INSERT INTO coding_task (id, hospital_id, visit_id, pipeline_version, status, retry_count, coding_stage, started_at, completed_at, created_at, updated_at)
    VALUES (@task_id, @hospital_id, @visit_id, N'phase1-v2.2-lite', 3, 0, 2, @now, @now, @now, @now);

    DECLARE @samples TABLE (original nvarchar(500), code nvarchar(64), title nvarchar(300), ev1 nvarchar(1000), ev2 nvarchar(1000), ord int);
    INSERT INTO @samples VALUES
    (N'急性阑尾炎', N'K35.900', N'急性阑尾炎', N'术后病理：急性化脓性阑尾炎。', N'入院查体：右下腹压痛、反跳痛，麦氏点压痛阳性。', 1),
    (N'2型糖尿病', N'E11.900', N'2型糖尿病', N'空腹血糖 9.8mmol/L，糖化血红蛋白 8.9%。', N'出院诊断：2型糖尿病，二甲双胍控制血糖。', 2),
    (N'原发性高血压', N'I10.x00', N'原发性高血压', N'入院血压 165/95mmHg，既往高血压病史 5 年。', N'出院诊断：原发性高血压 3 级（极高危）。', 3),
    (N'胆囊结石伴慢性胆囊炎', N'K80.100', N'胆囊结石伴慢性胆囊炎', N'腹部超声：胆囊壁增厚毛糙，囊内多发强回声光团。', N'术中见胆囊多发结石，胆囊壁炎性增厚。', 4),
    (N'腰椎间盘突出症', N'M51.202', N'腰椎间盘突出症', N'腰椎 MRI：L4/5 椎间盘向左后方突出，压迫硬膜囊。', N'查体：腰 4/5 棘突旁压痛伴左下肢放射痛。', 5),
    (N'社区获得性肺炎', N'J18.900', N'肺炎', N'胸部 CT：右下肺斑片状渗出影。', N'咳嗽咳痰伴发热 3 天，白细胞 12.5×10^9/L。', 6);

    DECLARE @o nvarchar(500), @c nvarchar(64), @t nvarchar(300), @e1 nvarchar(1000), @e2 nvarchar(1000), @ord int, @input_id uniqueidentifier;
    DECLARE cur CURSOR LOCAL FOR SELECT original, code, title, ev1, ev2, ord FROM @samples ORDER BY ord;
    OPEN cur;
    FETCH NEXT FROM cur INTO @o, @c, @t, @e1, @e2, @ord;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @input_id = NEWID();
        INSERT INTO coding_diagnosis_input (id, hospital_id, coding_task_id, visit_id, source_type, original_text, diagnosis_order, is_principal, status, created_at, updated_at)
        VALUES (@input_id, @hospital_id, @task_id, @visit_id, N'FRONT_PAGE', @o, @ord, CASE WHEN @ord = 1 THEN 1 ELSE 0 END, N'ACTIVE', @now, @now);

        INSERT INTO coding_recommendation (id, hospital_id, coding_task_id, diagnosis_input_id, pipeline_version, recommendation_type, code, code_system_code, title, recall_channel, recall_score, rule_score, confidence_score, rank, review_status, lifecycle_status, outcome, created_at, updated_at)
        VALUES (NEWID(), @hospital_id, @task_id, @input_id, N'phase1-v2.2-lite', N'DIAGNOSIS', @c, N'ICD-10', @t, N'ICD_INDEX', 0.9, 1.0, 0.9, 1, N'PENDING', N'ACTIVE', N'ACCEPTED', @now, @now);

        DECLARE @eid uniqueidentifier = NEWID();
        INSERT INTO clinical_evidence (id, hospital_id, visit_id, coding_task_id, evidence_type, source_type, original_text, start_position, end_position, evidence_level, evidence_score, source_reliability, temporal_validity, text_completeness, status, created_at, updated_at)
        VALUES (@eid, @hospital_id, @visit_id, @task_id, N'DIAGNOSIS', N'DISCHARGE_NOTE', @e1, 0, LEN(@e1), 2, 0.95, 0.9, 1.0, 1.0, N'ACTIVE', @now, @now);
        SET @eid = NEWID();
        INSERT INTO clinical_evidence (id, hospital_id, visit_id, coding_task_id, evidence_type, source_type, original_text, start_position, end_position, evidence_level, evidence_score, source_reliability, temporal_validity, text_completeness, status, created_at, updated_at)
        VALUES (@eid, @hospital_id, @visit_id, @task_id, N'DIAGNOSIS', N'DISCHARGE_NOTE', @e2, 0, LEN(@e2), 2, 0.9, 0.9, 1.0, 1.0, N'ACTIVE', @now, @now);

        FETCH NEXT FROM cur INTO @o, @c, @t, @e1, @e2, @ord;
    END
    CLOSE cur; DEALLOCATE cur;
END
SELECT (SELECT COUNT(1) FROM coding_diagnosis_input WHERE hospital_id = @hospital_id) AS inputs,
       (SELECT COUNT(1) FROM coding_recommendation WHERE hospital_id = @hospital_id) AS recs,
       (SELECT COUNT(1) FROM clinical_evidence WHERE hospital_id = @hospital_id) AS evidences;
