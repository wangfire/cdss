SET QUOTED_IDENTIFIER ON;
DECLARE @h uniqueidentifier = 'ff840089-ea7d-4c40-bb4c-5d47b4b49956';
DECLARE @now datetimeoffset(7) = SYSUTCDATETIME();
INSERT INTO pipeline_trace (id, hospital_id, coding_task_id, trace_id, status, started_at, created_at, updated_at)
SELECT NEWID(), @h, t.id, LOWER(REPLACE(NEWID(),'-','')), 'Running', @now, @now, @now
FROM coding_task t
WHERE t.hospital_id = @h
  AND NOT EXISTS (SELECT 1 FROM pipeline_trace pt WHERE pt.coding_task_id = t.id);
SELECT COUNT(1) AS traces FROM pipeline_trace WHERE hospital_id = @h;