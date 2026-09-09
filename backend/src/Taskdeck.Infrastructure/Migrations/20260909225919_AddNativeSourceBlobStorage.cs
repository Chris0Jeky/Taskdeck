using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskdeck.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNativeSourceBlobStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StoredBlobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ByteSize = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoredBlobs", x => x.Id);
                    table.UniqueConstraint("AK_StoredBlobs_Id_OwnerUserId", x => new { x.Id, x.OwnerUserId });
                    table.ForeignKey(
                        name: "FK_StoredBlobs_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StoredBlobChunks",
                columns: table => new
                {
                    BlobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    Content = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoredBlobChunks", x => new { x.BlobId, x.Ordinal });
                    table.ForeignKey(
                        name: "FK_StoredBlobChunks_StoredBlobs_BlobId",
                        column: x => x.BlobId,
                        principalTable: "StoredBlobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StoredBlobReferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BlobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Modality = table.Column<int>(type: "INTEGER", nullable: false),
                    ReferrerKind = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ReferrerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AcquiredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoredBlobReferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoredBlobReferences_StoredBlobs_BlobId_OwnerUserId",
                        columns: x => new { x.BlobId, x.OwnerUserId },
                        principalTable: "StoredBlobs",
                        principalColumns: new[] { "Id", "OwnerUserId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoredBlobReferences_BlobId_OwnerUserId",
                table: "StoredBlobReferences",
                columns: new[] { "BlobId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_StoredBlobReferences_OwnerUserId_Modality",
                table: "StoredBlobReferences",
                columns: new[] { "OwnerUserId", "Modality" });

            migrationBuilder.CreateIndex(
                name: "IX_StoredBlobs_OwnerUserId_ContentHash",
                table: "StoredBlobs",
                columns: new[] { "OwnerUserId", "ContentHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StoredBlobChunks");

            migrationBuilder.DropTable(
                name: "StoredBlobReferences");

            migrationBuilder.DropTable(
                name: "StoredBlobs");
        }
    }
}
