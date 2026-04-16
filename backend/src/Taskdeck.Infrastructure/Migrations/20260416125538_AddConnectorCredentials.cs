using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskdeck.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConnectorCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConnectorCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConnectorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AuthMethod = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    EncryptedValue = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    KeyVersion = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1),
                    RotatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConnectorCredentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConnectorCredentials_IntegrationConnectors_ConnectorId",
                        column: x => x.ConnectorId,
                        principalTable: "IntegrationConnectors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConnectorCredentials_ConnectorId_UserId",
                table: "ConnectorCredentials",
                columns: new[] { "ConnectorId", "UserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConnectorCredentials");
        }
    }
}
