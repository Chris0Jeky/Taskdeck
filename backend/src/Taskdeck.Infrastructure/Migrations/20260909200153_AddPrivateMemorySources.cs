using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskdeck.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPrivateMemorySources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AnswerSourceAssetId",
                table: "WorkspaceMemoryRevisions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AnswerSourceAssetId",
                table: "WorkspaceMemories",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EvidenceSourceAssetId",
                table: "WorkspaceMemories",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceCaptureId",
                table: "WorkspaceMemories",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnswerSourceAssetId",
                table: "WorkspaceMemoryRevisions");

            migrationBuilder.DropColumn(
                name: "AnswerSourceAssetId",
                table: "WorkspaceMemories");

            migrationBuilder.DropColumn(
                name: "EvidenceSourceAssetId",
                table: "WorkspaceMemories");

            migrationBuilder.DropColumn(
                name: "SourceCaptureId",
                table: "WorkspaceMemories");
        }
    }
}
