using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Database.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "modules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ModuleName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    BaseEndpointPath = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ContainerId = table.Column<string>(type: "TEXT", nullable: true),
                    ContainerPort = table.Column<int>(type: "INTEGER", nullable: false),
                    StoragePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_modules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserEmail = table.Column<string>(type: "TEXT", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    AllowedScopes = table.Column<string>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "module_endpoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ModuleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    HttpMethod = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    EndpointPath = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_module_endpoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_module_endpoints_modules_ModuleId",
                        column: x => x.ModuleId,
                        principalTable: "modules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Token = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsRevoked = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_refresh_tokens_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_module_endpoints_ModuleId_HttpMethod_EndpointPath",
                table: "module_endpoints",
                columns: new[] { "ModuleId", "HttpMethod", "EndpointPath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_modules_BaseEndpointPath",
                table: "modules",
                column: "BaseEndpointPath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_modules_ContainerPort",
                table: "modules",
                column: "ContainerPort",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_modules_StoragePath",
                table: "modules",
                column: "StoragePath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_Token",
                table: "refresh_tokens",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_UserId",
                table: "refresh_tokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_users_UserEmail",
                table: "users",
                column: "UserEmail",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "module_endpoints");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "modules");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
