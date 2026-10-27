using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HospitalAi.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class ExpandPermissionLeaves : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 权限配置改为「菜单 → 功能 → 按钮」三级树后，旧的粗粒度码展开为按钮叶子码。
            // 依据升级前的授权口径补齐，避免升级瞬间丢权限。
            migrationBuilder.Sql(
                """
                INSERT INTO app_role_permission
                    (id, hospital_id, app_role_id, permission_code, created_at, updated_at)
                SELECT NEWID(), holder.hospital_id, holder.app_role_id, m.leaf,
                       SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()
                FROM (
                    SELECT DISTINCT hospital_id, app_role_id, permission_code
                    FROM app_role_permission
                    WHERE permission_code IN (
                        'coding.read',
                        'coding.review',
                        'knowledge.icd.manage',
                        'knowledge.cm3.manage',
                        'knowledge.tumor.manage',
                        'knowledge.synonym.manage',
                        'knowledge.write',
                        'user.manage'
                    )
                ) AS holder
                JOIN (VALUES
                    ('coding.read', 'knowledge.read'),
                    ('coding.review', 'coding.review.accept'),
                    ('coding.review', 'coding.review.modify'),
                    ('coding.review', 'coding.review.reject'),
                    ('coding.review', 'coding.caseEntry'),
                    ('knowledge.icd.manage', 'knowledge.icd.import'),
                    ('knowledge.icd.manage', 'knowledge.icd.create'),
                    ('knowledge.icd.manage', 'knowledge.icd.update'),
                    ('knowledge.icd.manage', 'knowledge.icd.delete'),
                    ('knowledge.cm3.manage', 'knowledge.cm3.import'),
                    ('knowledge.cm3.manage', 'knowledge.cm3.create'),
                    ('knowledge.cm3.manage', 'knowledge.cm3.update'),
                    ('knowledge.cm3.manage', 'knowledge.cm3.delete'),
                    ('knowledge.tumor.manage', 'knowledge.tumor.create'),
                    ('knowledge.tumor.manage', 'knowledge.tumor.update'),
                    ('knowledge.tumor.manage', 'knowledge.tumor.delete'),
                    ('knowledge.synonym.manage', 'knowledge.synonym.import'),
                    ('knowledge.synonym.manage', 'knowledge.synonym.create'),
                    ('knowledge.synonym.manage', 'knowledge.synonym.update'),
                    ('knowledge.synonym.manage', 'knowledge.synonym.delete'),
                    ('knowledge.write', 'knowledge.hospital.create'),
                    ('knowledge.write', 'knowledge.index.rebuild'),
                    ('user.manage', 'user.create'),
                    ('user.manage', 'user.status'),
                    ('user.manage', 'user.role.assign'),
                    ('user.manage', 'role.create'),
                    ('user.manage', 'role.permission')
                ) AS m(source_code, leaf)
                    ON m.source_code = holder.permission_code
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM app_role_permission AS existing
                    WHERE existing.hospital_id = holder.hospital_id
                      AND existing.app_role_id = holder.app_role_id
                      AND existing.permission_code = m.leaf
                );
                """);

            // 去掉树里不再存在的粗粒度按钮码；knowledge.write 保留为「知识库运维」功能入口。
            migrationBuilder.Sql(
                """
                DELETE FROM app_role_permission
                WHERE permission_code IN (
                    'knowledge.icd.manage',
                    'knowledge.cm3.manage',
                    'knowledge.tumor.manage',
                    'knowledge.synonym.manage'
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 回退：按叶子码恢复粗粒度码，再清掉本次新增的叶子码。
            migrationBuilder.Sql(
                """
                INSERT INTO app_role_permission
                    (id, hospital_id, app_role_id, permission_code, created_at, updated_at)
                SELECT DISTINCT NEWID(), holder.hospital_id, holder.app_role_id, m.coarse_code,
                       SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()
                FROM app_role_permission AS holder
                JOIN (VALUES
                    ('knowledge.icd.import', 'knowledge.icd.manage'),
                    ('knowledge.icd.create', 'knowledge.icd.manage'),
                    ('knowledge.icd.update', 'knowledge.icd.manage'),
                    ('knowledge.icd.delete', 'knowledge.icd.manage'),
                    ('knowledge.cm3.import', 'knowledge.cm3.manage'),
                    ('knowledge.cm3.create', 'knowledge.cm3.manage'),
                    ('knowledge.cm3.update', 'knowledge.cm3.manage'),
                    ('knowledge.cm3.delete', 'knowledge.cm3.manage'),
                    ('knowledge.tumor.create', 'knowledge.tumor.manage'),
                    ('knowledge.tumor.update', 'knowledge.tumor.manage'),
                    ('knowledge.tumor.delete', 'knowledge.tumor.manage'),
                    ('knowledge.synonym.import', 'knowledge.synonym.manage'),
                    ('knowledge.synonym.create', 'knowledge.synonym.manage'),
                    ('knowledge.synonym.update', 'knowledge.synonym.manage'),
                    ('knowledge.synonym.delete', 'knowledge.synonym.manage')
                ) AS m(leaf, coarse_code)
                    ON m.leaf = holder.permission_code
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM app_role_permission AS existing
                    WHERE existing.hospital_id = holder.hospital_id
                      AND existing.app_role_id = holder.app_role_id
                      AND existing.permission_code = m.coarse_code
                );

                DELETE FROM app_role_permission
                WHERE permission_code IN (
                    'coding.review.accept',
                    'coding.review.modify',
                    'coding.review.reject',
                    'coding.caseEntry',
                    'knowledge.read',
                    'knowledge.hospital.create',
                    'knowledge.index.rebuild',
                    'knowledge.codesystem.import',
                    'knowledge.rules.import',
                    'knowledge.icd.import',
                    'knowledge.icd.create',
                    'knowledge.icd.update',
                    'knowledge.icd.delete',
                    'knowledge.cm3.import',
                    'knowledge.cm3.create',
                    'knowledge.cm3.update',
                    'knowledge.cm3.delete',
                    'knowledge.tumor.create',
                    'knowledge.tumor.update',
                    'knowledge.tumor.delete',
                    'knowledge.synonym.import',
                    'knowledge.synonym.create',
                    'knowledge.synonym.update',
                    'knowledge.synonym.delete',
                    'user.create',
                    'user.status',
                    'user.role.assign',
                    'role.create',
                    'role.permission'
                );
                """);
        }
    }
}
