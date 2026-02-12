using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskdeck.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomationArchiveChatOpsEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ArchiveItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    EntityId = table.Column<Guid>(type: "TEXT", maxLength: 100, nullable: false),
                    BoardId = table.Column<Guid>(type: "TEXT", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ArchivedByUserId = table.Column<Guid>(type: "TEXT", maxLength: 100, nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SnapshotJson = table.Column<string>(type: "TEXT", nullable: false),
                    RestoreStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    RestoredAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RestoredByUserId = table.Column<Guid>(type: "TEXT", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArchiveItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AutomationProposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceType = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceReferenceId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    BoardId = table.Column<Guid>(type: "TEXT", maxLength: 100, nullable: true),
                    RequestedByUserId = table.Column<Guid>(type: "TEXT", maxLength: 100, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    RiskLevel = table.Column<int>(type: "INTEGER", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    DiffPreview = table.Column<string>(type: "TEXT", nullable: true),
                    ValidationIssues = table.Column<string>(type: "TEXT", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DecidedByUserId = table.Column<Guid>(type: "TEXT", maxLength: 100, nullable: true),
                    AppliedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FailureReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationProposals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChatSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", maxLength: 100, nullable: false),
                    BoardId = table.Column<Guid>(type: "TEXT", maxLength: 100, nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CommandRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TemplateName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "TEXT", maxLength: 100, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ExitCode = table.Column<int>(type: "INTEGER", nullable: true),
                    Truncated = table.Column<bool>(type: "INTEGER", nullable: false),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    OutputPreview = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommandRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AutomationProposalOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProposalId = table.Column<Guid>(type: "TEXT", maxLength: 100, nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    ActionType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    TargetType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    TargetId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Parameters = table.Column<string>(type: "TEXT", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ExpectedVersion = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationProposalOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AutomationProposalOperations_AutomationProposals_ProposalId",
                        column: x => x.ProposalId,
                        principalTable: "AutomationProposals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChatMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", maxLength: 100, nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    MessageType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ProposalId = table.Column<Guid>(type: "TEXT", maxLength: 100, nullable: true),
                    TokenUsage = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatMessages_ChatSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "ChatSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CommandRunLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommandRunId = table.Column<Guid>(type: "TEXT", maxLength: 100, nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Level = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: false),
                    Metadata = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommandRunLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommandRunLogs_CommandRuns_CommandRunId",
                        column: x => x.CommandRunId,
                        principalTable: "CommandRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArchiveItems_ArchivedAt",
                table: "ArchiveItems",
                column: "ArchivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ArchiveItems_ArchivedByUserId",
                table: "ArchiveItems",
                column: "ArchivedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ArchiveItems_BoardId",
                table: "ArchiveItems",
                column: "BoardId");

            migrationBuilder.CreateIndex(
                name: "IX_ArchiveItems_EntityType_EntityId",
                table: "ArchiveItems",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_ArchiveItems_RestoreStatus",
                table: "ArchiveItems",
                column: "RestoreStatus");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationProposalOperations_IdempotencyKey",
                table: "AutomationProposalOperations",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AutomationProposalOperations_ProposalId",
                table: "AutomationProposalOperations",
                column: "ProposalId");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationProposalOperations_ProposalId_Sequence",
                table: "AutomationProposalOperations",
                columns: new[] { "ProposalId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationProposals_BoardId",
                table: "AutomationProposals",
                column: "BoardId");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationProposals_CorrelationId",
                table: "AutomationProposals",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationProposals_ExpiresAt",
                table: "AutomationProposals",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationProposals_RequestedByUserId",
                table: "AutomationProposals",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationProposals_Status",
                table: "AutomationProposals",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_CreatedAt",
                table: "ChatMessages",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_ProposalId",
                table: "ChatMessages",
                column: "ProposalId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_SessionId",
                table: "ChatMessages",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatSessions_BoardId",
                table: "ChatSessions",
                column: "BoardId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatSessions_CreatedAt",
                table: "ChatSessions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ChatSessions_Status",
                table: "ChatSessions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ChatSessions_UserId",
                table: "ChatSessions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CommandRunLogs_CommandRunId",
                table: "CommandRunLogs",
                column: "CommandRunId");

            migrationBuilder.CreateIndex(
                name: "IX_CommandRunLogs_Level",
                table: "CommandRunLogs",
                column: "Level");

            migrationBuilder.CreateIndex(
                name: "IX_CommandRunLogs_Timestamp",
                table: "CommandRunLogs",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_CommandRuns_CorrelationId",
                table: "CommandRuns",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_CommandRuns_CreatedAt",
                table: "CommandRuns",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CommandRuns_RequestedByUserId",
                table: "CommandRuns",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CommandRuns_Status",
                table: "CommandRuns",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArchiveItems");

            migrationBuilder.DropTable(
                name: "AutomationProposalOperations");

            migrationBuilder.DropTable(
                name: "ChatMessages");

            migrationBuilder.DropTable(
                name: "CommandRunLogs");

            migrationBuilder.DropTable(
                name: "AutomationProposals");

            migrationBuilder.DropTable(
                name: "ChatSessions");

            migrationBuilder.DropTable(
                name: "CommandRuns");
        }
    }
}
