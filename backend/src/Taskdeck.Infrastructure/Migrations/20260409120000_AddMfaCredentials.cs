using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskdeck.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMfaCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MfaEnabled",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "MfaCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Secret = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    IsConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    RecoveryCodes = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfaCredentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfaCredentials_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MfaCredentials_UserId",
                table: "MfaCredentials",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MfaCredentials");

            migrationBuilder.DropColumn(
                name: "MfaEnabled",
                table: "Users");
        }
    }
}
