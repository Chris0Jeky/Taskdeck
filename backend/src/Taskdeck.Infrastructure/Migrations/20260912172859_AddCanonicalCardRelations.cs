using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskdeck.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCanonicalCardRelations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CardRelations",
                columns: table => new
                {
                    BoardId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceCardId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TargetCardId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RelationType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardRelations", x => new { x.BoardId, x.SourceCardId, x.TargetCardId, x.RelationType });
                    table.CheckConstraint("CK_CardRelations_Endpoints", "SourceCardId <> TargetCardId");
                    table.CheckConstraint("CK_CardRelations_Kind", "RelationType IN ('relates-to', 'blocks', 'duplicates', 'spawned-from')");
                    table.CheckConstraint("CK_CardRelations_SymmetricOrder", "RelationType <> 'relates-to' OR SourceCardId < TargetCardId");
                    table.ForeignKey(
                        name: "FK_CardRelations_BoardDependencies_BoardId",
                        column: x => x.BoardId,
                        principalTable: "BoardDependencies",
                        principalColumn: "BoardId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CardRelations_Cards_SourceCardId",
                        column: x => x.SourceCardId,
                        principalTable: "Cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CardRelations_Cards_TargetCardId",
                        column: x => x.TargetCardId,
                        principalTable: "Cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CardRelations_SourceCardId",
                table: "CardRelations",
                column: "SourceCardId");

            migrationBuilder.CreateIndex(
                name: "IX_CardRelations_TargetCardId",
                table: "CardRelations",
                column: "TargetCardId");

            // Legacy JSON uses PascalCase and lowercase GUIDs; EF's SQLite GUID values are
            // uppercase. Select actual surviving IDs and skip old hard-delete dangling links.
            migrationBuilder.Sql("""
                INSERT OR IGNORE INTO CardRelations (BoardId, SourceCardId, TargetCardId, RelationType)
                SELECT graph.BoardId, prerequisite.Id, dependent.Id, 'blocks'
                FROM BoardDependencies AS graph, json_each(graph.EdgesJson) AS edge
                JOIN Cards AS dependent ON lower(dependent.Id) = lower(json_extract(edge.value, '$.CardId'))
                JOIN Cards AS prerequisite ON lower(prerequisite.Id) = lower(json_extract(edge.value, '$.DependsOnCardId'))
                WHERE dependent.BoardId = graph.BoardId AND prerequisite.BoardId = graph.BoardId
                    AND dependent.Id <> prerequisite.Id;
                """);
            // Native SQLite DROP COLUMN preserves the header and its incoming FK rows; an EF
            // table rebuild here can cascade away the just-backfilled graph.
            migrationBuilder.Sql("ALTER TABLE BoardDependencies DROP COLUMN EdgesJson;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EdgesJson",
                table: "BoardDependencies",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            // Downgrade reconstructs the latest dependency state, including cleared/new
            // headers. Nondependency relation metadata is intentionally lost on downgrade.
            migrationBuilder.Sql("""
                UPDATE BoardDependencies SET EdgesJson = (
                    SELECT json_group_array(json_object('CardId', TargetCardId, 'DependsOnCardId', SourceCardId))
                    FROM (SELECT SourceCardId, TargetCardId FROM CardRelations
                        WHERE BoardId = BoardDependencies.BoardId AND RelationType = 'blocks'
                        ORDER BY SourceCardId, TargetCardId)
                );
                """);
            migrationBuilder.DropTable(name: "CardRelations");
        }
    }
}
