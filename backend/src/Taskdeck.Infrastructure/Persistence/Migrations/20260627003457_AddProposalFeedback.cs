using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskdeck.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProposalFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProposalFeedbacks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProposalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReportedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Reason = table.Column<int>(type: "INTEGER", nullable: false),
                    ReportedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProposalFeedbacks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProposalFeedbacks_AutomationProposals_ProposalId",
                        column: x => x.ProposalId,
                        principalTable: "AutomationProposals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProposalFeedbacks_ProposalId_ReportedByUserId",
                table: "ProposalFeedbacks",
                columns: new[] { "ProposalId", "ReportedByUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProposalFeedbacks_ReportedByUserId_CreatedAt",
                table: "ProposalFeedbacks",
                columns: new[] { "ReportedByUserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProposalFeedbacks");
        }
    }
}
