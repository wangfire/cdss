using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HospitalAi.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class RbacTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "app_role",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    hospital_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_role", x => x.id);
                    table.ForeignKey(
                        name: "FK_app_role_hospital_hospital_id",
                        column: x => x.hospital_id,
                        principalTable: "hospital",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "app_user",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    hospital_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    display_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_user", x => x.id);
                    table.ForeignKey(
                        name: "FK_app_user_hospital_hospital_id",
                        column: x => x.hospital_id,
                        principalTable: "hospital",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "app_user_role",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    hospital_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    app_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    app_role_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_user_role", x => x.id);
                    table.ForeignKey(
                        name: "FK_app_user_role_app_role_app_role_id",
                        column: x => x.app_role_id,
                        principalTable: "app_role",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_app_user_role_app_user_app_user_id",
                        column: x => x.app_user_id,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_app_user_role_hospital_hospital_id",
                        column: x => x.hospital_id,
                        principalTable: "hospital",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_app_role_hospital_code",
                table: "app_role",
                columns: new[] { "hospital_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_app_user_hospital_code",
                table: "app_user",
                columns: new[] { "hospital_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_app_user_role_app_role_id",
                table: "app_user_role",
                column: "app_role_id");

            migrationBuilder.CreateIndex(
                name: "IX_app_user_role_app_user_id",
                table: "app_user_role",
                column: "app_user_id");

            migrationBuilder.CreateIndex(
                name: "ux_app_user_role_hospital_user_role",
                table: "app_user_role",
                columns: new[] { "hospital_id", "app_user_id", "app_role_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "app_user_role");

            migrationBuilder.DropTable(
                name: "app_role");

            migrationBuilder.DropTable(
                name: "app_user");
        }
    }
}
