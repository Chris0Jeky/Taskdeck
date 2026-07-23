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
            // must be pinned — otherwise this deploy would silently re-introduce operations a
            // reviewer edited out. Status = 1 is ProposalStatus.Approved (persisted via
            // HasConversion<int>; enum order PendingReview=0, Approved=1, ...). PendingReview
            // proposals are deliberately NOT backfilled (approve pins them from now on) and
            // terminal statuses never reach the executor's materialization.
            //
            // Pin the latest revision saved AT OR BEFORE the proposal's decision time
            // (RevisedAt <= DecidedAt), NOT the unconditional latest. This PR's invariant is that
            // Apply executes exactly what the approver saw/approved; an Approved proposal with a
            // revision saved AFTER DecidedAt is precisely the race this PR closes, so backfilling
            // that later revision would execute content the reviewer never approved. When no
            // qualifying pre-decision revision exists the pin stays NULL and Apply runs the
            // original operations — again, what the approver saw.
            //
            // Timestamp formats differ and must not be compared lexicographically as-is: EF stores
            // DecidedAt (DateTime) as TEXT "yyyy-MM-dd HH:mm:ss.FFFFFFF" and RevisedAt
            // (DateTimeOffset) as "yyyy-MM-dd HH:mm:ss.FFFFFFF+00:00". Both are UTC, so appending
            // '+00:00' to DecidedAt yields an identically-shaped string; the fractional-second
            // parts then compare correctly digit-by-digit because the shorter fraction's next
            // character ('+') sorts below any digit. A NULL DecidedAt (never expected for an
            // Approved row) concatenates to NULL, the comparison yields NULL, no revision
            // qualifies, and the pin stays NULL. Proven empirically by
            // MigrationBootstrapTests.AddApprovedRevisionId_* (before/after/straddle cases).
            migrationBuilder.Sql(
                """
                UPDATE AutomationProposals
                SET ApprovedRevisionId = (
                    SELECT pr.Id
                    FROM ProposalRevisions pr
                    WHERE pr.ProposalId = AutomationProposals.Id
                      AND pr.RevisedAt <= (AutomationProposals.DecidedAt || '+00:00')
                    ORDER BY pr.RevisedAt DESC, pr.RevisionNumber DESC
                    LIMIT 1)
                WHERE Status = 1
                  AND EXISTS (
                    SELECT 1
                    FROM ProposalRevisions pr2
                    WHERE pr2.ProposalId = AutomationProposals.Id
                      AND pr2.RevisedAt <= (AutomationProposals.DecidedAt || '+00:00'));
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
