using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskdeck.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConnectorCredentialUserForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ConnectorCredentials_UserId",
                table: "ConnectorCredentials",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ConnectorCredentials_Users_UserId",
                table: "ConnectorCredentials",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ConnectorCredentials_Users_UserId",
                table: "ConnectorCredentials");

            migrationBuilder.DropIndex(
                name: "IX_ConnectorCredentials_UserId",
                table: "ConnectorCredentials");
        }
    }
}
