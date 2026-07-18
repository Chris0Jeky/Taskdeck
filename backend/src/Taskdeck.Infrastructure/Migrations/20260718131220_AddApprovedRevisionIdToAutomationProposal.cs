using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskdeck.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovedRevisionIdToAutomationProposal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedRevisionId",
                table: "AutomationProposals",
                type: "TEXT",
                nullable: true);

            // Backfill (#1428 Apply parity for in-flight proposals): before this migration, Apply
            // materialized the LATEST revision for an Approved proposal. A null pin now means
            // "apply the ORIGINAL operations", so proposals already Approved with saved revisions
            // must be pinned to their latest revision — otherwise this deploy would silently
            // re-introduce operations a reviewer edited out. Status = 1 is ProposalStatus.Approved
            // (persisted via HasConversion<int>; enum order PendingReview=0, Approved=1, ...).
            // PendingReview proposals are deliberately NOT backfilled (approve pins them from now
            // on) and terminal statuses never reach the executor's materialization.
            migrationBuilder.Sql(
                """
                UPDATE AutomationProposals
                SET ApprovedRevisionId = (
                    SELECT pr.Id
                    FROM ProposalRevisions pr
                    WHERE pr.ProposalId = AutomationProposals.Id
                    ORDER BY pr.RevisionNumber DESC
                    LIMIT 1)
                WHERE Status = 1
                  AND EXISTS (
                    SELECT 1
                    FROM ProposalRevisions pr2
                    WHERE pr2.ProposalId = AutomationProposals.Id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApprovedRevisionId",
                table: "AutomationProposals");
        }
    }
}
