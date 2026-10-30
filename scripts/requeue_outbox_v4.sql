SET QUOTED_IDENTIFIER ON;
DECLARE @h uniqueidentifier = 'ff840089-ea7d-4c40-bb4c-5d47b4b49956';
DECLARE @now datetimeoffset(7) = SYSUTCDATETIME();
DELETE FROM outbox_message WHERE hospital_id = @h;
INSERT INTO outbox_message (id, hospital_id, message_type, payload_json, occurred_at, published_at, created_at, updated_at)
SELECT o.id, @h, 'coding.task.created',
       '{"messageId":"' + CONVERT(varchar(36), o.id) + '","hospitalId":"' + CONVERT(varchar(36), @h) + '","taskId":"' + CONVERT(varchar(36), t.id) + '","visitId":"' + CONVERT(varchar(36), t.visit_id) + '","pipelineVersion":"phase1-v2.2-lite","traceId":"' + pt.trace_id + '"}',
       @now, NULL, @now, @now
FROM coding_task t
JOIN pipeline_trace pt ON pt.coding_task_id = t.id AND pt.hospital_id = @h
CROSS JOIN (SELECT NEWID() AS id) o
WHERE t.hospital_id = @h;
SELECT COUNT(1) AS queued FROM outbox_message WHERE hospital_id = @h;