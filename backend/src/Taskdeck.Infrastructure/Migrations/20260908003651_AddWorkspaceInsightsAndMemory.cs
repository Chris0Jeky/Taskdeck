using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskdeck.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspaceInsightsAndMemory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QuietInsights",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BoardId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CardId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MemoryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Rule = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    TargetKey = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Detail = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    Evidence = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    CheckedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SnoozeUntil = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuietInsights", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuietInsights_Boards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "Boards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QuietInsights_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkspaceMemories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BoardId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InsightId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OriginalEvidence = table.Column<string>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 240, nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    OriginalText = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Archived = table.Column<bool>(type: "INTEGER", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceMemories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkspaceMemories_Boards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "Boards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkspaceMemories_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkspaceMemoryRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MemoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 240, nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    Archived = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceMemoryRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkspaceMemoryRevisions_WorkspaceMemories_MemoryId",
                        column: x => x.MemoryId,
                        principalTable: "WorkspaceMemories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuietInsights_BoardId",
                table: "QuietInsights",
                column: "BoardId");

            migrationBuilder.CreateIndex(
                name: "IX_QuietInsights_UserId_BoardId_Rule_TargetKey",
                table: "QuietInsights",
                columns: new[] { "UserId", "BoardId", "Rule", "TargetKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceMemories_BoardId",
                table: "WorkspaceMemories",
                column: "BoardId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceMemories_UserId_BoardId",
                table: "WorkspaceMemories",
                columns: new[] { "UserId", "BoardId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceMemoryRevisions_MemoryId_Revision",
                table: "WorkspaceMemoryRevisions",
                columns: new[] { "MemoryId", "Revision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuietInsights");

            migrationBuilder.DropTable(
                name: "WorkspaceMemoryRevisions");

            migrationBuilder.DropTable(
                name: "WorkspaceMemories");
        }
    }
}
