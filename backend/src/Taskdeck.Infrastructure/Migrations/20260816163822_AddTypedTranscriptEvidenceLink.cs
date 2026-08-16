using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskdeck.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTypedTranscriptEvidenceLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TranscriptId",
                table: "ProvenanceEvidenceLinks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProvenanceEvidenceLinks_TranscriptId",
                table: "ProvenanceEvidenceLinks",
                column: "TranscriptId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProvenanceEvidenceLinks_TranscriptId",
                table: "ProvenanceEvidenceLinks",
                sql: "(\"SourceType\" = 'Transcript' AND \"TranscriptId\" IS NOT NULL) OR (\"SourceType\" <> 'Transcript' AND \"TranscriptId\" IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_ProvenanceEvidenceLinks_Transcripts_TranscriptId",
                table: "ProvenanceEvidenceLinks",
                column: "TranscriptId",
                principalTable: "Transcripts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProvenanceEvidenceLinks_Transcripts_TranscriptId",
                table: "ProvenanceEvidenceLinks");

            migrationBuilder.DropIndex(
                name: "IX_ProvenanceEvidenceLinks_TranscriptId",
                table: "ProvenanceEvidenceLinks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProvenanceEvidenceLinks_TranscriptId",
                table: "ProvenanceEvidenceLinks");

            migrationBuilder.DropColumn(
                name: "TranscriptId",
                table: "ProvenanceEvidenceLinks");
        }
    }
}
