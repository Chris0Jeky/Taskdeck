using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskdeck.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspaceTodayAndOnboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OnboardingCompletedAt",
                table: "UserPreferences",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OnboardingDismissedAt",
                table: "UserPreferences",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OnboardingVisibility",
                table: "UserPreferences",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OnboardingCompletedAt",
                table: "UserPreferences");

            migrationBuilder.DropColumn(
                name: "OnboardingDismissedAt",
                table: "UserPreferences");

            migrationBuilder.DropColumn(
                name: "OnboardingVisibility",
                table: "UserPreferences");
        }
    }
}
