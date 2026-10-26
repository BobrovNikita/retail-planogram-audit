using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailVision.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AuditRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAtUtc",
                table: "Audits",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PendingPhotoDeletion",
                columns: table => new
                {
                    Path = table.Column<string>(type: "TEXT", nullable: false),
                    QueuedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingPhotoDeletion", x => x.Path);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Audits_ArchivedAtUtc",
                table: "Audits",
                column: "ArchivedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PendingPhotoDeletion");

            migrationBuilder.DropIndex(
                name: "IX_Audits_ArchivedAtUtc",
                table: "Audits");

            migrationBuilder.DropColumn(
                name: "ArchivedAtUtc",
                table: "Audits");
        }
    }
}
