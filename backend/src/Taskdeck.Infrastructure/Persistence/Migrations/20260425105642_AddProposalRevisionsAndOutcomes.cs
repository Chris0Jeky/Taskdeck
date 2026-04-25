using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskdeck.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProposalRevisionsAndOutcomes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProposalOutcomes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProposalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OutcomeType = table.Column<int>(type: "INTEGER", nullable: false),
                    DecidedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProposalOutcomes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProposalOutcomes_AutomationProposals_ProposalId",
                        column: x => x.ProposalId,
                        principalTable: "AutomationProposals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProposalRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProposalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RevisionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    EditorUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RevisedPayload = table.Column<string>(type: "TEXT", nullable: false),
                    RevisedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProposalRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProposalRevisions_AutomationProposals_ProposalId",
                        column: x => x.ProposalId,
                        principalTable: "AutomationProposals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProposalOutcomes_DecidedByUserId",
                table: "ProposalOutcomes",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProposalOutcomes_ProposalId",
                table: "ProposalOutcomes",
                column: "ProposalId");

            migrationBuilder.CreateIndex(
                name: "IX_ProposalRevisions_EditorUserId",
                table: "ProposalRevisions",
                column: "EditorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProposalRevisions_ProposalId",
                table: "ProposalRevisions",
                column: "ProposalId");

            migrationBuilder.CreateIndex(
                name: "IX_ProposalRevisions_ProposalId_RevisionNumber",
                table: "ProposalRevisions",
                columns: new[] { "ProposalId", "RevisionNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProposalOutcomes");

            migrationBuilder.DropTable(
                name: "ProposalRevisions");
        }
    }
}
