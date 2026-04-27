using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskdeck.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProvenanceEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProposalProvenances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProposalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModelId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    TotalTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProposalProvenances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProvenanceFields",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FieldName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Confidence = table.Column<double>(type: "REAL", nullable: false),
                    ExtractiveQuote = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ProposalProvenanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProvenanceFields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProvenanceFields_ProposalProvenances_ProposalProvenanceId",
                        column: x => x.ProposalProvenanceId,
                        principalTable: "ProposalProvenances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProvenanceEvidenceLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SourceId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SpanStart = table.Column<int>(type: "INTEGER", nullable: true),
                    SpanEnd = table.Column<int>(type: "INTEGER", nullable: true),
                    ProvenanceFieldId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProvenanceEvidenceLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProvenanceEvidenceLinks_ProvenanceFields_ProvenanceFieldId",
                        column: x => x.ProvenanceFieldId,
                        principalTable: "ProvenanceFields",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProposalProvenances_ProposalId",
                table: "ProposalProvenances",
                column: "ProposalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProvenanceEvidenceLinks_ProvenanceFieldId",
                table: "ProvenanceEvidenceLinks",
                column: "ProvenanceFieldId");

            migrationBuilder.CreateIndex(
                name: "IX_ProvenanceFields_ProposalProvenanceId",
                table: "ProvenanceFields",
                column: "ProposalProvenanceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProvenanceEvidenceLinks");

            migrationBuilder.DropTable(
                name: "ProvenanceFields");

            migrationBuilder.DropTable(
                name: "ProposalProvenances");
        }
    }
}
