using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskdeck.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLlmUsageReservationLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExpiresAt",
                table: "LlmUsageRecords",
                type: "TEXT",
                nullable: true);

            // Backfill every pre-existing usage row to Committed (1). This is a one-time column default
            // for the ALTER only; the model deliberately configures no store-generated default so the
            // app always writes Status explicitly (Reserved=0 for reservations, Committed=1 otherwise).
            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "LlmUsageRecords",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_LlmUsageRecords_Status_ExpiresAt",
                table: "LlmUsageRecords",
                columns: new[] { "Status", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LlmUsageRecords_Status_ExpiresAt",
                table: "LlmUsageRecords");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "LlmUsageRecords");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "LlmUsageRecords");
        }
    }
}
