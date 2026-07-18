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
