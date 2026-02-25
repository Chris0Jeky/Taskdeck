using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskdeck.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboundWebhookModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutboundWebhookSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BoardId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EndpointUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    SigningSecret = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    EventFilters = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    RevokedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LastTriggeredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboundWebhookSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutboundWebhookSubscriptions_Boards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "Boards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OutboundWebhookSubscriptions_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OutboundWebhookDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BoardId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Payload = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastResponseStatusCode = table.Column<int>(type: "INTEGER", nullable: true),
                    LastErrorMessage = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboundWebhookDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutboundWebhookDeliveries_OutboundWebhookSubscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "OutboundWebhookSubscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutboundWebhookDeliveries_CreatedAt",
                table: "OutboundWebhookDeliveries",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_OutboundWebhookDeliveries_Status_NextAttemptAt",
                table: "OutboundWebhookDeliveries",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboundWebhookDeliveries_SubscriptionId",
                table: "OutboundWebhookDeliveries",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboundWebhookSubscriptions_BoardId_IsActive",
                table: "OutboundWebhookSubscriptions",
                columns: new[] { "BoardId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboundWebhookSubscriptions_CreatedAt",
                table: "OutboundWebhookSubscriptions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_OutboundWebhookSubscriptions_CreatedByUserId",
                table: "OutboundWebhookSubscriptions",
                column: "CreatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutboundWebhookDeliveries");

            migrationBuilder.DropTable(
                name: "OutboundWebhookSubscriptions");
        }
    }
}
