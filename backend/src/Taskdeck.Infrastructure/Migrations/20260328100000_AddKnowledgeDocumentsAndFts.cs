using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskdeck.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKnowledgeDocumentsAndFts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KnowledgeDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BoardId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "TEXT", maxLength: 50000, nullable: false),
                    SourceType = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Tags = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeDocuments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KnowledgeChunks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChunkIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    Metadata = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeChunks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KnowledgeChunks_KnowledgeDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "KnowledgeDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeDocuments_UserId",
                table: "KnowledgeDocuments",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeDocuments_BoardId",
                table: "KnowledgeDocuments",
                column: "BoardId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeDocuments_UserId_IsArchived",
                table: "KnowledgeDocuments",
                columns: new[] { "UserId", "IsArchived" });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeChunks_DocumentId",
                table: "KnowledgeChunks",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeChunks_DocumentId_ChunkIndex",
                table: "KnowledgeChunks",
                columns: new[] { "DocumentId", "ChunkIndex" },
                unique: true);

            // Create FTS5 virtual table for full-text search
            migrationBuilder.Sql(
                @"CREATE VIRTUAL TABLE IF NOT EXISTS KnowledgeDocumentsFts
                  USING fts5(title, content, document_id UNINDEXED);");

            // Trigger to populate FTS on INSERT
            migrationBuilder.Sql(
                @"CREATE TRIGGER IF NOT EXISTS KnowledgeDocuments_ai AFTER INSERT ON KnowledgeDocuments
                  BEGIN
                      INSERT INTO KnowledgeDocumentsFts(title, content, document_id)
                      VALUES (new.Title, new.Content, new.Id);
                  END;");

            // Trigger to update FTS on UPDATE (FTS5 requires special delete command)
            migrationBuilder.Sql(
                @"CREATE TRIGGER IF NOT EXISTS KnowledgeDocuments_au AFTER UPDATE ON KnowledgeDocuments
                  BEGIN
                      INSERT INTO KnowledgeDocumentsFts(KnowledgeDocumentsFts, title, content, document_id)
                      VALUES ('delete', old.Title, old.Content, old.Id);
                      INSERT INTO KnowledgeDocumentsFts(title, content, document_id)
                      VALUES (new.Title, new.Content, new.Id);
                  END;");

            // Trigger to clean FTS on DELETE (FTS5 requires special delete command)
            migrationBuilder.Sql(
                @"CREATE TRIGGER IF NOT EXISTS KnowledgeDocuments_ad AFTER DELETE ON KnowledgeDocuments
                  BEGIN
                      INSERT INTO KnowledgeDocumentsFts(KnowledgeDocumentsFts, title, content, document_id)
                      VALUES ('delete', old.Title, old.Content, old.Id);
                  END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS KnowledgeDocuments_ad;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS KnowledgeDocuments_au;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS KnowledgeDocuments_ai;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS KnowledgeDocumentsFts;");

            migrationBuilder.DropTable(name: "KnowledgeChunks");
            migrationBuilder.DropTable(name: "KnowledgeDocuments");
        }
    }
}
