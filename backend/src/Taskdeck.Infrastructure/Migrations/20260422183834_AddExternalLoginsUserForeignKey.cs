using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskdeck.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalLoginsUserForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: FK_ExternalLogins_Users_UserId was already added
            // in migration 20260402230822_AddTokenInvalidatedAt. This migration is
            // retained as a no-op because it may already be recorded in
            // __EFMigrationsHistory on existing databases.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: the FK this migration originally added was already
            // present from 20260402230822_AddTokenInvalidatedAt, so there is nothing
            // to reverse here.
        }
    }
}
