SET QUOTED_IDENTIFIER ON;
DECLARE @h uniqueidentifier = '6b427cb5-a6ba-4a8e-9b36-311c1102f20b';
DECLARE @now datetimeoffset(7) = SYSUTCDATETIME();
DECLARE @rid uniqueidentifier;
IF NOT EXISTS (SELECT 1 FROM app_role WHERE hospital_id=@h AND code='ADMIN')
    INSERT INTO app_role (id, hospital_id, code, name, description, created_at, updated_at)
    VALUES (NEWID(), @h, 'ADMIN', N'管理员', N'内置', @now, @now);
SET @rid = (SELECT id FROM app_role WHERE hospital_id=@h AND code='ADMIN');
IF NOT EXISTS (SELECT 1 FROM app_user WHERE hospital_id=@h AND code='admin')
BEGIN
    DECLARE @uid uniqueidentifier = NEWID();
    INSERT INTO app_user (id, hospital_id, code, display_name, status, password_hash, failed_login_count, created_at, updated_at)
    VALUES (@uid, @h, 'admin', N'管理员', 'ACTIVE', '', 0, @now, @now);
    INSERT INTO app_user_role (id, hospital_id, app_user_id, app_role_id, created_at, updated_at)
    VALUES (NEWID(), @h, @uid, @rid, @now, @now);
END
SELECT code, status FROM app_user WHERE hospital_id=@h;