using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskdeck.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableManualRepresentations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Representations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CaptureId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    ParentSourceAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ParentRepresentationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProcessingRunId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProcessorId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ProcessorVersion = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ProcessorModel = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ConfigurationHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Language = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    QualityState = table.Column<int>(type: "INTEGER", nullable: false),
                    Warnings = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Representations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Representations_Captures_CaptureId",
                        column: x => x.CaptureId,
                        principalTable: "Captures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Representations_Representations_ParentRepresentationId",
                        column: x => x.ParentRepresentationId,
                        principalTable: "Representations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Representations_SourceAssets_ParentSourceAssetId",
                        column: x => x.ParentSourceAssetId,
                        principalTable: "SourceAssets",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Representations_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RepresentationSupersessions",
                columns: table => new
                {
                    RepresentationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SupersededByRepresentationId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepresentationSupersessions", x => x.RepresentationId);
                    table.ForeignKey(
                        name: "FK_RepresentationSupersessions_Representations_RepresentationId",
                        column: x => x.RepresentationId,
                        principalTable: "Representations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RepresentationSupersessions_Representations_SupersededByRepresentationId",
                        column: x => x.SupersededByRepresentationId,
                        principalTable: "Representations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Representations_CaptureId",
                table: "Representations",
                column: "CaptureId");

            migrationBuilder.CreateIndex(
                name: "IX_Representations_ParentRepresentationId",
                table: "Representations",
                column: "ParentRepresentationId");

            migrationBuilder.CreateIndex(
                name: "IX_Representations_ParentSourceAssetId",
                table: "Representations",
                column: "ParentSourceAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_Representations_UserId_CaptureId",
                table: "Representations",
                columns: new[] { "UserId", "CaptureId" });

            migrationBuilder.CreateIndex(
                name: "IX_RepresentationSupersessions_SupersededByRepresentationId",
                table: "RepresentationSupersessions",
                column: "SupersededByRepresentationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RepresentationSupersessions");

            migrationBuilder.DropTable(
                name: "Representations");
        }
    }
}
