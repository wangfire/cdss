SET QUOTED_IDENTIFIER ON;
DECLARE @h uniqueidentifier = 'ff840089-ea7d-4c40-bb4c-5d47b4b49956';
DECLARE @now datetimeoffset(7) = SYSUTCDATETIME();
UPDATE o SET payload_json =
  '{"messageId":"' + CONVERT(varchar(36), o.id) + '","hospitalId":"' + CONVERT(varchar(36), o.hospital_id) + '","taskId":"' + CONVERT(varchar(36), t.id) + '","visitId":"' + CONVERT(varchar(36), t.visit_id) + '","pipelineVersion":"phase1-v2.2-lite","traceId":"' + REPLACE(NEWID(),'-','') + '"}',
  o.updated_at = @now
FROM outbox_message o
JOIN coding_task t ON t.hospital_id = o.hospital_id
   AND t.id = TRY_CAST(SUBSTRING(o.payload_json, CHARINDEX('"taskId":"', o.payload_json) + 10, 36) AS uniqueidentifier)
WHERE o.hospital_id = @h AND o.published_at IS NULL;
SELECT COUNT(1) AS fixed FROM outbox_message WHERE hospital_id = @h AND published_at IS NULL;