using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskdeck.Infrastructure.Migrations
{
    /// <summary>
    /// Context Fabric reconciliation pass (ADR-0065 amended 2026-08-30 after the external audit of
    /// PR #2280). Reshapes the scaffolded <c>Captures</c> table — one lifecycle enum becomes three
    /// orthogonal axes (user disposition, processing summary, action state), the producer gains a
    /// principal id and loses the <c>Import</c> kind, the intent splits into requested and
    /// effective, and <c>LegacySource</c> is named for what it is (a snapshot) — and adds the
    /// general <c>SourceAssets</c> / <c>SourceAssetTextPayloads</c> tables. The table is empty on
    /// every install that never enabled <c>ContextFabric:DualWriteCaptures</c>; the data statements
    /// below keep an install that did enable it honest rather than positionally re-mapped (the
    /// scaffolder's generated renames paired unrelated columns).
    /// </summary>
    public partial class ReconcileContextFabricScaffold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Renames that keep their meaning.
            migrationBuilder.RenameColumn(
                name: "Producer",
                table: "Captures",
                newName: "ProducerKind");

            migrationBuilder.RenameColumn(
                name: "Intent",
                table: "Captures",
                newName: "RequestedIntent");

            migrationBuilder.RenameColumn(
                name: "LegacySource",
                table: "Captures",
                newName: "LegacySourceSnapshot");

            // 2. New axes and identity columns.
            migrationBuilder.AddColumn<int>(
                name: "Disposition",
                table: "Captures",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ProcessingSummary",
                table: "Captures",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ActionState",
                table: "Captures",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EffectiveIntent",
                table: "Captures",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "IntentResolvedByRunId",
                table: "Captures",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProducedByPrincipalId",
                table: "Captures",
                type: "TEXT",
                nullable: true);

            // 3. Carry existing rows (only present where the dual-write flag was on) across honestly.
            //    Legacy Lifecycle: Received 0, Preparing 1, Understood 2, Routed 3, NeedsReview 4,
            //    Acted 5, Kept 6, Failed 7, Archived 8.
            //    Disposition: Active 0, Kept 1, Archived 2.
            //    ProcessingSummary: Idle 0, Processing 1, Partial 2, Ready 3, Failed 4.
            //    ActionState: Unplanned 0, NeedsInput 1, NeedsReview 2, Acted 3.
            //    ProducerKind: the retired Import (3) becomes Human (0) — importing is a transport.
            //    RequestedIntent Auto (3) leaves EffectiveIntent null; every other value copies across.
            migrationBuilder.Sql(
                """
                UPDATE "Captures" SET
                    "Disposition" = CASE "Lifecycle" WHEN 6 THEN 1 WHEN 8 THEN 2 ELSE 0 END,
                    "ProcessingSummary" = CASE "Lifecycle" WHEN 1 THEN 1 WHEN 2 THEN 3 WHEN 3 THEN 3 WHEN 4 THEN 3 WHEN 5 THEN 3 WHEN 7 THEN 4 ELSE 0 END,
                    "ActionState" = CASE "Lifecycle" WHEN 4 THEN 2 WHEN 5 THEN 3 ELSE 0 END,
                    "ProducerKind" = CASE "ProducerKind" WHEN 3 THEN 0 ELSE "ProducerKind" END,
                    "EffectiveIntent" = CASE "RequestedIntent" WHEN 3 THEN NULL ELSE "RequestedIntent" END;
                """);

            // 4. The single lifecycle column is retired.
            migrationBuilder.DropIndex(
                name: "IX_Captures_UserId_Lifecycle",
                table: "Captures");

            migrationBuilder.DropColumn(
                name: "Lifecycle",
                table: "Captures");

            migrationBuilder.CreateIndex(
                name: "IX_Captures_UserId_Disposition",
                table: "Captures",
                columns: new[] { "UserId", "Disposition" });

            // 5. The general source model between Capture and Representation.
            migrationBuilder.CreateTable(
                name: "SourceAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CaptureId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    Modality = table.Column<int>(type: "INTEGER", nullable: false),
                    MediaType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ByteSize = table.Column<long>(type: "INTEGER", nullable: false),
                    StorageKind = table.Column<int>(type: "INTEGER", nullable: false),
                    BlobReferenceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LegacyArtefactId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ExternalReference = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    OriginalName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourceAssets_Captures_CaptureId",
                        column: x => x.CaptureId,
                        principalTable: "Captures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SourceAssetTextPayloads",
                columns: table => new
                {
                    SourceAssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 200000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceAssetTextPayloads", x => x.SourceAssetId);
                    table.ForeignKey(
                        name: "FK_SourceAssetTextPayloads_SourceAssets_SourceAssetId",
                        column: x => x.SourceAssetId,
                        principalTable: "SourceAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SourceAssets_CaptureId_Ordinal",
                table: "SourceAssets",
                columns: new[] { "CaptureId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourceAssets_ContentHash",
                table: "SourceAssets",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_SourceAssets_LegacyArtefactId",
                table: "SourceAssets",
                column: "LegacyArtefactId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SourceAssetTextPayloads");

            migrationBuilder.DropTable(
                name: "SourceAssets");

            migrationBuilder.DropIndex(
                name: "IX_Captures_UserId_Disposition",
                table: "Captures");

            migrationBuilder.AddColumn<int>(
                name: "Lifecycle",
                table: "Captures",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Fold the three axes back into the single legacy enum (lossy by construction; the
            // scaffold table is empty on an install that never enabled dual-write).
            migrationBuilder.Sql(
                """
                UPDATE "Captures" SET "Lifecycle" =
                    CASE
                        WHEN "Disposition" = 2 THEN 8
                        WHEN "ActionState" = 3 THEN 5
                        WHEN "Disposition" = 1 THEN 6
                        WHEN "ActionState" = 2 THEN 4
                        WHEN "ProcessingSummary" = 4 THEN 7
                        WHEN "ProcessingSummary" = 1 THEN 1
                        WHEN "ProcessingSummary" IN (2, 3) THEN 2
                        ELSE 0
                    END;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Captures_UserId_Lifecycle",
                table: "Captures",
                columns: new[] { "UserId", "Lifecycle" });

            migrationBuilder.DropColumn(
                name: "ProducedByPrincipalId",
                table: "Captures");

            migrationBuilder.DropColumn(
                name: "IntentResolvedByRunId",
                table: "Captures");

            migrationBuilder.DropColumn(
                name: "EffectiveIntent",
                table: "Captures");

            migrationBuilder.DropColumn(
                name: "ActionState",
                table: "Captures");

            migrationBuilder.DropColumn(
                name: "ProcessingSummary",
                table: "Captures");

            migrationBuilder.DropColumn(
                name: "Disposition",
                table: "Captures");

            migrationBuilder.RenameColumn(
                name: "LegacySourceSnapshot",
                table: "Captures",
                newName: "LegacySource");

            migrationBuilder.RenameColumn(
                name: "RequestedIntent",
                table: "Captures",
                newName: "Intent");

            migrationBuilder.RenameColumn(
                name: "ProducerKind",
                table: "Captures",
                newName: "Producer");
        }
    }
}
