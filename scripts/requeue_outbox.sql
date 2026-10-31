SET QUOTED_IDENTIFIER ON;
DECLARE @h uniqueidentifier = 'ff840089-ea7d-4c40-bb4c-5d47b4b49956';
DECLARE @now datetimeoffset(7) = SYSUTCDATETIME();
INSERT INTO outbox_message (id, hospital_id, message_type, payload_json, occurred_at, published_at, created_at, updated_at)
SELECT NEWID(), @h, 'CodingTaskCreated:CodingTaskCreatedMessage',
       '{"messageId":"' + CONVERT(varchar(36), NEWID()) + '","hospitalId":"' + CONVERT(varchar(36), @h) + '","taskId":"' + CONVERT(varchar(36), t.id) + '","visitId":"' + CONVERT(varchar(36), t.visit_id) + '","pipelineVersion":"phase1-v2.2-lite","traceId":"' + REPLACE(NEWID(),'-','') + '"}',
       @now, NULL, @now, @now
FROM coding_task t WHERE t.hospital_id = @h;
SELECT COUNT(1) AS outbox_rows FROM outbox_message WHERE hospital_id = @h;