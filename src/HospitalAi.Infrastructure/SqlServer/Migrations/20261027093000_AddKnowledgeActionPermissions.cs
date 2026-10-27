using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HospitalAi.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddKnowledgeActionPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 知识库写操作由 knowledge.write 拆成按钮级权限码。
            // 升级前悬授予 knowledge.write 的角色，按原口径补齐全部按钮权限，避免升级即失权。
            migrationBuilder.Sql(
                """
                INSERT INTO app_role_permission
                    (id, hospital_id, app_role_id, permission_code, created_at, updated_at)
                SELECT NEWID(), holder.hospital_id, holder.app_role_id, p.permission_code,
                       SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()
                FROM (
                    SELECT DISTINCT hospital_id, app_role_id
                    FROM app_role_permission
                    WHERE permission_code = 'knowledge.write'
                ) AS holder
                CROSS JOIN (VALUES
                    ('knowledge.codesystem.import'),
                    ('knowledge.rules.import'),
                    ('knowledge.icd.manage'),
                    ('knowledge.cm3.manage'),
                    ('knowledge.tumor.manage'),
                    ('knowledge.synonym.manage')
                ) AS p(permission_code)
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM app_role_permission AS existing
                    WHERE existing.hospital_id = holder.hospital_id
                      AND existing.app_role_id = holder.app_role_id
                      AND existing.permission_code = p.permission_code
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM app_role_permission
                WHERE permission_code IN (
                    'knowledge.codesystem.import',
                    'knowledge.rules.import',
                    'knowledge.icd.manage',
                    'knowledge.cm3.manage',
                    'knowledge.tumor.manage',
                    'knowledge.synonym.manage'
                );
                """);
        }
    }
}
