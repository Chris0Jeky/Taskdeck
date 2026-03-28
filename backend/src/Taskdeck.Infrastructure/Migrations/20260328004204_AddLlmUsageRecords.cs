using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskdeck.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLlmUsageRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LlmUsageRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Surface = table.Column<int>(type: "INTEGER", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    InputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    OutputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LlmUsageRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LlmUsageRecords_CreatedAt",
                table: "LlmUsageRecords",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_LlmUsageRecords_Surface_CreatedAt",
                table: "LlmUsageRecords",
                columns: new[] { "Surface", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LlmUsageRecords_UserId",
                table: "LlmUsageRecords",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_LlmUsageRecords_UserId_CreatedAt",
                table: "LlmUsageRecords",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LlmUsageRecords");
        }
    }
}
