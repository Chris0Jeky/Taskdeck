using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskdeck.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLlmRequestTranscriptLinkage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TranscriptId",
                table: "LlmRequests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LlmRequests_TranscriptId",
                table: "LlmRequests",
                column: "TranscriptId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_LlmRequests_Transcripts_TranscriptId",
                table: "LlmRequests",
                column: "TranscriptId",
                principalTable: "Transcripts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LlmRequests_Transcripts_TranscriptId",
                table: "LlmRequests");

            migrationBuilder.DropIndex(
                name: "IX_LlmRequests_TranscriptId",
                table: "LlmRequests");

            migrationBuilder.DropColumn(
                name: "TranscriptId",
                table: "LlmRequests");
        }
    }
}
