SET QUOTED_IDENTIFIER ON;
DECLARE @h uniqueidentifier = 'ff840089-ea7d-4c40-bb4c-5d47b4b49956';
DECLARE @now datetimeoffset(7) = SYSUTCDATETIME();
DELETE FROM outbox_message WHERE hospital_id = @h;
-- messageId 必须等于 outbox 行 id（RabbitMqEventBus 校验），payload 用行 id 生成。
INSERT INTO outbox_message (id, hospital_id, message_type, payload_json, occurred_at, published_at, created_at, updated_at)
SELECT o.id, @h, 'coding.task.created',
       '{"messageId":"' + CONVERT(varchar(36), o.id) + '","hospitalId":"' + CONVERT(varchar(36), @h) + '","taskId":"' + CONVERT(varchar(36), t.id) + '","visitId":"' + CONVERT(varchar(36), t.visit_id) + '","pipelineVersion":"phase1-v2.2-lite","traceId":"' + REPLACE(NEWID(),'-','') + '"}',
       @now, NULL, @now, @now
FROM coding_task t
JOIN (SELECT NEWID() AS id) o ON 1 = 1
WHERE t.hospital_id = @h;
UPDATE coding_task SET status = 0, started_at = NULL, completed_at = NULL, error_code = NULL WHERE hospital_id = @h;
SELECT COUNT(1) AS queued FROM outbox_message WHERE hospital_id = @h;
