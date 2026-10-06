using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modulix.Database.Migrations
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
                    ModuleEntryAssemblyFileName = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_modules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "module_endpoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ModuleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    HttpMethod = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    EndpointPath = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "module_endpoints");

            migrationBuilder.DropTable(
                name: "modules");
        }
    }
}
