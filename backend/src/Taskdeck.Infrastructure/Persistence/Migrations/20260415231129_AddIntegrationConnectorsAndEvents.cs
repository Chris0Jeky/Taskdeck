using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskdeck.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrationConnectorsAndEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IntegrationConnectors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ConnectorType = table.Column<int>(type: "INTEGER", nullable: false),
                    Direction = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Configuration = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationConnectors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IntegrationConnectors_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConnectorEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConnectorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventType = table.Column<int>(type: "INTEGER", nullable: false),
                    Payload = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConnectorEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConnectorEvents_IntegrationConnectors_ConnectorId",
                        column: x => x.ConnectorId,
                        principalTable: "IntegrationConnectors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConnectorEvents_ConnectorId",
                table: "ConnectorEvents",
                column: "ConnectorId");

            migrationBuilder.CreateIndex(
                name: "IX_ConnectorEvents_ConnectorId_CreatedAt",
                table: "ConnectorEvents",
                columns: new[] { "ConnectorId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationConnectors_CreatedAt",
                table: "IntegrationConnectors",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationConnectors_UserId",
                table: "IntegrationConnectors",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationConnectors_UserId_Status",
                table: "IntegrationConnectors",
                columns: new[] { "UserId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConnectorEvents");

            migrationBuilder.DropTable(
                name: "IntegrationConnectors");
        }
    }
}
