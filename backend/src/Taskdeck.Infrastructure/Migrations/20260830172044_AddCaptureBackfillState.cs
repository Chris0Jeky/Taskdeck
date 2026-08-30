using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskdeck.Infrastructure.Migrations
{
    /// <summary>
    /// Schema for CF-01 (<c>#2255</c>): the marker that records whether the ID-preserving capture
    /// backfill has finished on this database, and the supersession links that let a post-intake
    /// edit append a corrected <c>SourceAsset</c> instead of rewriting an immutable one.
    /// <para>
    /// The marker gates the Inbox read switch. Without it a restart could not tell "this database
    /// has no legacy capture rows" from "this database has not been migrated yet", and reading the
    /// durable aggregate on the second would drop captures out of the Inbox. The backfill itself
    /// carries no state here: it is an anti-join over capture-shaped queue rows with no capture, so
    /// it is idempotent and resumable on its own, and <c>Down</c> losing this row only costs one
    /// re-scan rather than any data.
    /// </para>
    /// <para>
    /// Purely additive: two nullable columns, one index and one new table. Nothing is renamed, so
    /// there is no scaffolder rename-pairing to review, and <c>Down</c> is the exact inverse.
    /// </para>
    /// </summary>
    public partial class AddCaptureBackfillState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SupersededByAssetId",
                table: "SourceAssets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SupersedesAssetId",
                table: "SourceAssets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CaptureBackfillStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    MigratedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SkippedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastSkipReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaptureBackfillStates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SourceAssets_SupersedesAssetId",
                table: "SourceAssets",
                column: "SupersedesAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_CaptureBackfillStates_Key",
                table: "CaptureBackfillStates",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CaptureBackfillStates");

            migrationBuilder.DropIndex(
                name: "IX_SourceAssets_SupersedesAssetId",
                table: "SourceAssets");

            migrationBuilder.DropColumn(
                name: "SupersededByAssetId",
                table: "SourceAssets");

            migrationBuilder.DropColumn(
                name: "SupersedesAssetId",
                table: "SourceAssets");
        }
    }
}
