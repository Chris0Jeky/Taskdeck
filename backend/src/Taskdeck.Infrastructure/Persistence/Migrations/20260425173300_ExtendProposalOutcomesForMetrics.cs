using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Taskdeck.Infrastructure.Persistence;

#nullable disable

namespace Taskdeck.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(TaskdeckDbContext))]
    [Migration("20260425173300_ExtendProposalOutcomesForMetrics")]
    public partial class ExtendProposalOutcomesForMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "AverageFieldConfidence",
                table: "ProposalOutcomes",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Decision",
                table: "ProposalOutcomes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("""
                UPDATE ProposalOutcomes
                SET Decision = CASE OutcomeType
                    WHEN 0 THEN 0
                    WHEN 1 THEN 1
                    WHEN 2 THEN 2
                    WHEN 3 THEN 3
                    ELSE 0
                END
                """);

            migrationBuilder.AddColumn<double>(
                name: "DecisionLatencySeconds",
                table: "ProposalOutcomes",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "EditedFieldCount",
                table: "ProposalOutcomes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FieldCount",
                table: "ProposalOutcomes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Backfill edit counts for legacy EditedThenApproved rows so they
            // don't sit at 0/0 (which contradicts the "edited" semantics and
            // would skew edit-rate analytics). We use 1 as a safe non-zero
            // sentinel because exact legacy counts are not recoverable.
            migrationBuilder.Sql("""
                UPDATE ProposalOutcomes
                SET EditedFieldCount = 1,
                    FieldCount = 1
                WHERE OutcomeType = 1
                """);

            migrationBuilder.AddColumn<string>(
                name: "ModelId",
                table: "ProposalOutcomes",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RiskLevel",
                table: "ProposalOutcomes",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.AddColumn<string>(
                name: "SourceType",
                table: "ProposalOutcomes",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.CreateIndex(
                name: "IX_ProposalOutcomes_CreatedAt",
                table: "ProposalOutcomes",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ProposalOutcomes_Decision",
                table: "ProposalOutcomes",
                column: "Decision");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProposalOutcomes_CreatedAt",
                table: "ProposalOutcomes");

            migrationBuilder.DropIndex(
                name: "IX_ProposalOutcomes_Decision",
                table: "ProposalOutcomes");

            migrationBuilder.DropColumn(
                name: "AverageFieldConfidence",
                table: "ProposalOutcomes");

            migrationBuilder.DropColumn(
                name: "Decision",
                table: "ProposalOutcomes");

            migrationBuilder.DropColumn(
                name: "DecisionLatencySeconds",
                table: "ProposalOutcomes");

            migrationBuilder.DropColumn(
                name: "EditedFieldCount",
                table: "ProposalOutcomes");

            migrationBuilder.DropColumn(
                name: "FieldCount",
                table: "ProposalOutcomes");

            migrationBuilder.DropColumn(
                name: "ModelId",
                table: "ProposalOutcomes");

            migrationBuilder.DropColumn(
                name: "RiskLevel",
                table: "ProposalOutcomes");

            migrationBuilder.DropColumn(
                name: "SourceType",
                table: "ProposalOutcomes");
        }
    }
}
