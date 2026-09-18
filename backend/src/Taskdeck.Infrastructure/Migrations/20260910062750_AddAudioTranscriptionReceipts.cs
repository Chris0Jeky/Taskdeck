using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskdeck.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAudioTranscriptionReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AudioTranscriptionAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AudioAnswerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CaptureId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceAssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequestId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequestHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ConfigurationHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Deadline = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    FailureCode = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    RepresentationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AudioTranscriptionAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AudioTranscriptionAttempts_Captures_CaptureId",
                        column: x => x.CaptureId,
                        principalTable: "Captures",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AudioTranscriptionAttempts_Representations_RepresentationId",
                        column: x => x.RepresentationId,
                        principalTable: "Representations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AudioTranscriptionAttempts_SourceAssets_SourceAssetId",
                        column: x => x.SourceAssetId,
                        principalTable: "SourceAssets",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AudioTranscriptionAttempts_ThinkingAudioAnswers_AudioAnswerId",
                        column: x => x.AudioAnswerId,
                        principalTable: "ThinkingAudioAnswers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AudioTranscriptionAttempts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AudioTranscriptionBudgets",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UtcDay = table.Column<int>(type: "INTEGER", nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    InputBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AudioTranscriptionBudgets", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_AudioTranscriptionBudgets_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AudioTranscriptionAttempts_AudioAnswerId",
                table: "AudioTranscriptionAttempts",
                column: "AudioAnswerId");

            migrationBuilder.CreateIndex(
                name: "IX_AudioTranscriptionAttempts_CaptureId",
                table: "AudioTranscriptionAttempts",
                column: "CaptureId");

            migrationBuilder.CreateIndex(
                name: "IX_AudioTranscriptionAttempts_RepresentationId",
                table: "AudioTranscriptionAttempts",
                column: "RepresentationId");

            migrationBuilder.CreateIndex(
                name: "IX_AudioTranscriptionAttempts_SourceAssetId",
                table: "AudioTranscriptionAttempts",
                column: "SourceAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_AudioTranscriptionAttempts_UserId_AudioAnswerId",
                table: "AudioTranscriptionAttempts",
                columns: new[] { "UserId", "AudioAnswerId" });

            migrationBuilder.CreateIndex(
                name: "IX_AudioTranscriptionAttempts_UserId_RequestId",
                table: "AudioTranscriptionAttempts",
                columns: new[] { "UserId", "RequestId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AudioTranscriptionAttempts");

            migrationBuilder.DropTable(
                name: "AudioTranscriptionBudgets");
        }
    }
}
