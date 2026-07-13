using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskdeck.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRegistrationGating : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RegistrationBootstraps",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegistrationBootstraps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RegistrationInvites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CodeHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DisplayPrefix = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegistrationInvites", x => x.Id);
                });

            // Existing installations already have at least one real account and
            // must not regain the first-user bootstrap after this migration.
            // Fresh databases leave the singleton absent so the first successful
            // registration can claim it transactionally.
            migrationBuilder.Sql(
                """
                INSERT INTO "RegistrationBootstraps" ("Id", "ClaimedAt")
                SELECT 'registration', CURRENT_TIMESTAMP
                WHERE EXISTS (
                    SELECT 1 FROM "Users"
                    WHERE "Email" <> 'cli@system.taskdeck'
                );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationInvites_CodeHash",
                table: "RegistrationInvites",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationInvites_ConsumedAt_ExpiresAt",
                table: "RegistrationInvites",
                columns: new[] { "ConsumedAt", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RegistrationBootstraps");

            migrationBuilder.DropTable(
                name: "RegistrationInvites");
        }
    }
}
