using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskdeck.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddApiKeyScopes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Scopes",
                table: "ApiKeys",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "ApiKeys"
                SET "Scopes" = 7
                WHERE "Scopes" IS NULL;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "Scopes",
                table: "ApiKeys",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Scopes",
                table: "ApiKeys");
        }
    }
}
