using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HospitalAi.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// 正式权限矩阵初始化：按内置角色模板（CODER / DOCTOR / REVIEWER / KNOWLEDGE_MANAGER / USER_ADMIN）
    /// 为每家医院已存在的同名角色回填缺失的模板权限，保证权限矩阵在存量环境一次收敛到位。
    /// 模板的权威定义在应用层 RoleMatrix.BuiltIn（迁移内以 SQL 内联同一份授权口径，新增权限码时同步维护）；
    /// ADMIN 全权不落库，无需回填。旧阶段角色码 COLLECTOR 不在模板内，不回填、由管理端收敛。
    /// </summary>
    public partial class SeedRoleMatrix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 权限集临时表：模板码 → 权限码（与 RoleMatrix.BuiltIn 保持同口径）。
            migrationBuilder.Sql("""
                CREATE TABLE #role_matrix (template_code nvarchar(64) not null, permission_code nvarchar(64) not null);

                INSERT INTO #role_matrix (template_code, permission_code) VALUES
                -- CODER：编码员（读 + 全量审核按钮 + 病例录入）
                ('CODER', 'coding.read'),
                ('CODER', 'coding.review'),
                ('CODER', 'coding.review.accept'),
                ('CODER', 'coding.review.modify'),
                ('CODER', 'coding.review.reject'),
                ('CODER', 'coding.caseEntry'),
                -- DOCTOR：临床医生（只读 + 病例录入）
                ('DOCTOR', 'coding.read'),
                ('DOCTOR', 'coding.caseEntry'),
                -- REVIEWER：病案审核（读 + 审核功能，不含病例录入）
                ('REVIEWER', 'coding.read'),
                ('REVIEWER', 'coding.review'),
                -- KNOWLEDGE_MANAGER：知识库管理
                ('KNOWLEDGE_MANAGER', 'knowledge.read'),
                ('KNOWLEDGE_MANAGER', 'knowledge.write'),
                ('KNOWLEDGE_MANAGER', 'knowledge.hospital.create'),
                ('KNOWLEDGE_MANAGER', 'knowledge.index.rebuild'),
                ('KNOWLEDGE_MANAGER', 'knowledge.codesystem.import'),
                ('KNOWLEDGE_MANAGER', 'knowledge.rules.import'),
                ('KNOWLEDGE_MANAGER', 'knowledge.icd.import'),
                ('KNOWLEDGE_MANAGER', 'knowledge.icd.create'),
                ('KNOWLEDGE_MANAGER', 'knowledge.icd.update'),
                ('KNOWLEDGE_MANAGER', 'knowledge.icd.delete'),
                ('KNOWLEDGE_MANAGER', 'knowledge.cm3.import'),
                ('KNOWLEDGE_MANAGER', 'knowledge.cm3.create'),
                ('KNOWLEDGE_MANAGER', 'knowledge.cm3.update'),
                ('KNOWLEDGE_MANAGER', 'knowledge.cm3.delete'),
                ('KNOWLEDGE_MANAGER', 'knowledge.tumor.create'),
                ('KNOWLEDGE_MANAGER', 'knowledge.tumor.update'),
                ('KNOWLEDGE_MANAGER', 'knowledge.tumor.delete'),
                ('KNOWLEDGE_MANAGER', 'knowledge.synonym.import'),
                ('KNOWLEDGE_MANAGER', 'knowledge.synonym.create'),
                ('KNOWLEDGE_MANAGER', 'knowledge.synonym.update'),
                ('KNOWLEDGE_MANAGER', 'knowledge.synonym.delete'),
                -- USER_ADMIN：账户管理（不含 role.permission 写权，防止单角色自提权闭环）
                ('USER_ADMIN', 'user.manage'),
                ('USER_ADMIN', 'user.create'),
                ('USER_ADMIN', 'user.status'),
                ('USER_ADMIN', 'user.role.assign'),
                ('USER_ADMIN', 'role.create');

                -- 幂等回填：对每家医院，凡模板码角色已存在则补齐缺失的模板权限（不改动已配置的其他权限）。
                INSERT INTO app_role_permission
                (id, hospital_id, app_role_id, permission_code, created_at, updated_at)
                SELECT NEWID(), r.hospital_id, r.id, m.permission_code,
                       SYSUTCDATETIME(), SYSUTCDATETIME()
                FROM app_role r
                JOIN #role_matrix m ON m.template_code = r.code
                WHERE NOT EXISTS (
                    SELECT 1 FROM app_role_permission existing
                    WHERE existing.hospital_id = r.hospital_id
                      AND existing.app_role_id = r.id
                      AND existing.permission_code = m.permission_code);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 回填只增不改：无法区分“模板补齐”与“人工配置”的既有行，
            // Down 不删除任何数据（角色权限可随时在管理端收敛），保持迁移可逆性说明。
        }
    }
}
