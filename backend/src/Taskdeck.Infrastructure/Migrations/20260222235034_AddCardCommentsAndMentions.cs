using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskdeck.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCardCommentsAndMentions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CardComments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CardId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BoardId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ParentCommentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    EditedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Content = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CardComments_CardComments_ParentCommentId",
                        column: x => x.ParentCommentId,
                        principalTable: "CardComments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CardComments_Cards_CardId",
                        column: x => x.CardId,
                        principalTable: "Cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CardComments_Users_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CardCommentMentions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CardCommentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MentionedUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MentionedUsername = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardCommentMentions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CardCommentMentions_CardComments_CardCommentId",
                        column: x => x.CardCommentId,
                        principalTable: "CardComments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CardCommentMentions_Users_MentionedUserId",
                        column: x => x.MentionedUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CardCommentMentions_CardCommentId",
                table: "CardCommentMentions",
                column: "CardCommentId");

            migrationBuilder.CreateIndex(
                name: "IX_CardCommentMentions_CardCommentId_MentionedUserId",
                table: "CardCommentMentions",
                columns: new[] { "CardCommentId", "MentionedUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CardCommentMentions_MentionedUserId",
                table: "CardCommentMentions",
                column: "MentionedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CardComments_AuthorUserId",
                table: "CardComments",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CardComments_BoardId",
                table: "CardComments",
                column: "BoardId");

            migrationBuilder.CreateIndex(
                name: "IX_CardComments_CardId",
                table: "CardComments",
                column: "CardId");

            migrationBuilder.CreateIndex(
                name: "IX_CardComments_CardId_CreatedAt",
                table: "CardComments",
                columns: new[] { "CardId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CardComments_ParentCommentId",
                table: "CardComments",
                column: "ParentCommentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CardCommentMentions");

            migrationBuilder.DropTable(
                name: "CardComments");
        }
    }
}
