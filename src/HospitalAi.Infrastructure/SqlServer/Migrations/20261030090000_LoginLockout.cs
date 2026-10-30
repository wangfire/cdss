using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HospitalAi.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// 登录锁定（防暴力破解）：
    /// failed_login_count 连续失败计数（成功登录清零）；
    /// locked_until 锁定截止时间（UTC，空 = 未锁定）；lockout_at 最近一次触发锁定的时间（审计）。
    /// 锁定策略由应用配置 Security:Lockout 决定（默认 5 次失败锁 15 分钟），本迁移只落持久化列。
    /// </summary>
    public partial class LoginLockout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "failed_login_count",
                table: "app_user",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset?>(
                name: "locked_until",
                table: "app_user",
                type: "datetimeoffset(7)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset?>(
                name: "lockout_at",
                table: "app_user",
                type: "datetimeoffset(7)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_app_user_locked_until",
                table: "app_user",
                column: "locked_until");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_app_user_locked_until",
                table: "app_user");

            migrationBuilder.DropColumn(
                name: "lockout_at",
                table: "app_user");

            migrationBuilder.DropColumn(
                name: "locked_until",
                table: "app_user");

            migrationBuilder.DropColumn(
                name: "failed_login_count",
                table: "app_user");
        }
    }
}
