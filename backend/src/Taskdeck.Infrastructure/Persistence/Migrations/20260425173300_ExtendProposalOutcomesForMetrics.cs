using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskdeck.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
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
