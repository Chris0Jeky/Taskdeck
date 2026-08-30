using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskdeck.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCaptureAggregate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Captures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CapturedAtServer = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CapturedAtClient = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    PrimaryModality = table.Column<int>(type: "INTEGER", nullable: false),
                    OriginAdapter = table.Column<int>(type: "INTEGER", nullable: false),
                    Producer = table.Column<int>(type: "INTEGER", nullable: false),
                    Intent = table.Column<int>(type: "INTEGER", nullable: false),
                    Lifecycle = table.Column<int>(type: "INTEGER", nullable: false),
                    LegacySource = table.Column<int>(type: "INTEGER", nullable: false),
                    ContextBoardId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UserTitle = table.Column<string>(type: "TEXT", maxLength: 240, nullable: true),
                    UserNote = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    LegacyRequestId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Captures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Captures_Boards_ContextBoardId",
                        column: x => x.ContextBoardId,
                        principalTable: "Boards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Captures_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Captures_ContextBoardId",
                table: "Captures",
                column: "ContextBoardId");

            migrationBuilder.CreateIndex(
                name: "IX_Captures_LegacyRequestId",
                table: "Captures",
                column: "LegacyRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Captures_UserId_CreatedAt",
                table: "Captures",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Captures_UserId_Lifecycle",
                table: "Captures",
                columns: new[] { "UserId", "Lifecycle" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Captures");
        }
    }
}
