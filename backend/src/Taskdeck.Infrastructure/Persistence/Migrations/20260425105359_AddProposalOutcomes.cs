using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskdeck.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProposalOutcomes : Migration
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
                    DecidedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Decision = table.Column<int>(type: "INTEGER", nullable: false),
                    DecisionLatencySeconds = table.Column<double>(type: "REAL", nullable: false),
                    FieldCount = table.Column<int>(type: "INTEGER", nullable: false),
                    EditedFieldCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    RiskLevel = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ModelId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    AverageFieldConfidence = table.Column<double>(type: "REAL", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProposalOutcomes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProposalOutcomes_CreatedAt",
                table: "ProposalOutcomes",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ProposalOutcomes_DecidedByUserId",
                table: "ProposalOutcomes",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProposalOutcomes_Decision",
                table: "ProposalOutcomes",
                column: "Decision");

            migrationBuilder.CreateIndex(
                name: "IX_ProposalOutcomes_ProposalId",
                table: "ProposalOutcomes",
                column: "ProposalId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProposalOutcomes");
        }
    }
}
