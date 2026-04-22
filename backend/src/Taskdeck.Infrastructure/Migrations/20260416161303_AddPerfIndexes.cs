using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskdeck.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPerfIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Cards_BoardId",
                table: "Cards");

            migrationBuilder.CreateIndex(
                name: "IX_Cards_BoardId_ColumnId",
                table: "Cards",
                columns: new[] { "BoardId", "ColumnId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityId_Timestamp",
                table: "AuditLogs",
                columns: new[] { "EntityId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserId_Timestamp",
                table: "AuditLogs",
                columns: new[] { "UserId", "Timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Cards_BoardId_ColumnId",
                table: "Cards");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_EntityId_Timestamp",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_UserId_Timestamp",
                table: "AuditLogs");

            migrationBuilder.CreateIndex(
                name: "IX_Cards_BoardId",
                table: "Cards",
                column: "BoardId");
        }
    }
}
