using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HospitalAi.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddRolePermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "app_role_permission",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    hospital_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    app_role_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    permission_code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_role_permission", x => x.id);
                    table.ForeignKey(
                        name: "FK_app_role_permission_app_role_app_role_id",
                        column: x => x.app_role_id,
                        principalTable: "app_role",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_app_role_permission_hospital_hospital_id",
                        column: x => x.hospital_id,
                        principalTable: "hospital",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_app_role_permission_app_role_id",
                table: "app_role_permission",
                column: "app_role_id");

            migrationBuilder.CreateIndex(
                name: "ux_app_role_permission_hospital_role_code",
                table: "app_role_permission",
                columns: new[] { "hospital_id", "app_role_id", "permission_code" },
                unique: true);

            // 内置角色沿用升级前的角色码口径；ADMIN 隐式拥有全部权限，不落库。
            migrationBuilder.Sql(
                """
                INSERT INTO app_role_permission
                    (id, hospital_id, app_role_id, permission_code, created_at, updated_at)
                SELECT NEWID(), r.hospital_id, r.id, p.permission_code,
                       SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()
                FROM app_role AS r
                CROSS JOIN (VALUES
                    ('coding.read'), ('coding.review'), ('knowledge.write')
                ) AS p(permission_code)
                WHERE UPPER(r.code) = 'CODER';

                INSERT INTO app_role_permission
                    (id, hospital_id, app_role_id, permission_code, created_at, updated_at)
                SELECT NEWID(), r.hospital_id, r.id, p.permission_code,
                       SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()
                FROM app_role AS r
                CROSS JOIN (VALUES
                    ('coding.read'), ('coding.review')
                ) AS p(permission_code)
                WHERE UPPER(r.code) IN ('DOCTOR', 'COLLECTOR');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "app_role_permission");
        }
    }
}
