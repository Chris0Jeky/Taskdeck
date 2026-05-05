using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskdeck.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProposalProvenanceForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_ProposalProvenances_AutomationProposals_ProposalId",
                table: "ProposalProvenances",
                column: "ProposalId",
                principalTable: "AutomationProposals",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProposalProvenances_AutomationProposals_ProposalId",
                table: "ProposalProvenances");
        }
    }
}
