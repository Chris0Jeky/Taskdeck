using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskdeck.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddThinkingQuestionMemorySource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SourceCardId",
                table: "WorkspaceMemories",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SourceDeckRevision",
                table: "WorkspaceMemories",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceLayerId",
                table: "WorkspaceMemories",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceQuestionHash",
                table: "WorkspaceMemories",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceMemories_UserId_SourceCardId_SourceLayerId_SourceQuestionHash",
                table: "WorkspaceMemories",
                columns: new[] { "UserId", "SourceCardId", "SourceLayerId", "SourceQuestionHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkspaceMemories_UserId_SourceCardId_SourceLayerId_SourceQuestionHash",
                table: "WorkspaceMemories");

            migrationBuilder.DropColumn(
                name: "SourceCardId",
                table: "WorkspaceMemories");

            migrationBuilder.DropColumn(
                name: "SourceDeckRevision",
                table: "WorkspaceMemories");

            migrationBuilder.DropColumn(
                name: "SourceLayerId",
                table: "WorkspaceMemories");

            migrationBuilder.DropColumn(
                name: "SourceQuestionHash",
                table: "WorkspaceMemories");
        }
    }
}
