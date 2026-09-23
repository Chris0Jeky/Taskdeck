using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskdeck.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStoredBlobReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StoredBlobReservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Modality = table.Column<int>(type: "INTEGER", nullable: false),
                    ByteSize = table.Column<long>(type: "INTEGER", nullable: false),
                    ReferrerKind = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ReferrerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoredBlobReservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoredBlobReservations_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoredBlobReservations_ExpiresAtUtc",
                table: "StoredBlobReservations",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_StoredBlobReservations_OwnerUserId_ExpiresAtUtc",
                table: "StoredBlobReservations",
                columns: new[] { "OwnerUserId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StoredBlobReservations_OwnerUserId_Modality_ExpiresAtUtc",
                table: "StoredBlobReservations",
                columns: new[] { "OwnerUserId", "Modality", "ExpiresAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StoredBlobReservations");
        }
    }
}
