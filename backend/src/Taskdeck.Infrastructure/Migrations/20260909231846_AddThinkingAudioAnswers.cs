using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskdeck.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddThinkingAudioAnswers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ThinkingAudioAnswers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BoardId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CardId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuestionHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CaptureId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceAssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UploadId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    RepresentationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ConfirmedMemoryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ThinkingAudioAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ThinkingAudioAnswers_Boards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "Boards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ThinkingAudioAnswers_Captures_CaptureId",
                        column: x => x.CaptureId,
                        principalTable: "Captures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ThinkingAudioAnswers_Representations_RepresentationId",
                        column: x => x.RepresentationId,
                        principalTable: "Representations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ThinkingAudioAnswers_SourceAssets_SourceAssetId",
                        column: x => x.SourceAssetId,
                        principalTable: "SourceAssets",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ThinkingAudioAnswers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ThinkingAudioAnswers_WorkspaceMemories_ConfirmedMemoryId",
                        column: x => x.ConfirmedMemoryId,
                        principalTable: "WorkspaceMemories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ThinkingAudioAnswers_BoardId",
                table: "ThinkingAudioAnswers",
                column: "BoardId");

            migrationBuilder.CreateIndex(
                name: "IX_ThinkingAudioAnswers_CaptureId",
                table: "ThinkingAudioAnswers",
                column: "CaptureId");

            migrationBuilder.CreateIndex(
                name: "IX_ThinkingAudioAnswers_ConfirmedMemoryId",
                table: "ThinkingAudioAnswers",
                column: "ConfirmedMemoryId");

            migrationBuilder.CreateIndex(
                name: "IX_ThinkingAudioAnswers_RepresentationId",
                table: "ThinkingAudioAnswers",
                column: "RepresentationId");

            migrationBuilder.CreateIndex(
                name: "IX_ThinkingAudioAnswers_SourceAssetId",
                table: "ThinkingAudioAnswers",
                column: "SourceAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ThinkingAudioAnswers_UserId_CardId_LayerId_QuestionHash",
                table: "ThinkingAudioAnswers",
                columns: new[] { "UserId", "CardId", "LayerId", "QuestionHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ThinkingAudioAnswers_UserId_UploadId",
                table: "ThinkingAudioAnswers",
                columns: new[] { "UserId", "UploadId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ThinkingAudioAnswers");
        }
    }
}
