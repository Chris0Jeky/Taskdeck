using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskdeck.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddArtefactExtractions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ArtefactExtractions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceArtefactId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExtractorName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ExtractorVersion = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    WarningsJson = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    ExtractedText = table.Column<string>(type: "TEXT", maxLength: 102400, nullable: false),
                    TextLength = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArtefactExtractions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArtefactExtractions_SourceArtefacts_SourceArtefactId",
                        column: x => x.SourceArtefactId,
                        principalTable: "SourceArtefacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArtefactExtractions_SourceArtefactId_CreatedAt",
                table: "ArtefactExtractions",
                columns: new[] { "SourceArtefactId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArtefactExtractions");
        }
    }
}
