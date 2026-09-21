using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HospitalAi.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AppUserPasswordHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "password_hash",
                table: "app_user",
                type: "nvarchar(512)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset?>(
                name: "last_login_at",
                table: "app_user",
                type: "datetimeoffset(7)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "password_hash",
                table: "app_user");

            migrationBuilder.DropColumn(
                name: "last_login_at",
                table: "app_user");
        }
    }
}
