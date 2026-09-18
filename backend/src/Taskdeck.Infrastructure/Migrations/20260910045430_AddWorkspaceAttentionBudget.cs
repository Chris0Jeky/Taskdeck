using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskdeck.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspaceAttentionBudget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttentionJson",
                table: "UserPreferences",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AttentionRevision",
                table: "UserPreferences",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttentionJson",
                table: "UserPreferences");

            migrationBuilder.DropColumn(
                name: "AttentionRevision",
                table: "UserPreferences");
        }
    }
}
