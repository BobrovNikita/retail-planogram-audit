using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailVision.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AuditHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "PlanogramId",
                table: "Audits",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<int>(
                name: "ConfirmedViolations",
                table: "Audits",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CurrentDetectionsJson",
                table: "Audits",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "DetectionCount",
                table: "Audits",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "HasCorrections",
                table: "Audits",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ModelName",
                table: "Audits",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ModelSha256",
                table: "Audits",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "OriginalPercent",
                table: "Audits",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "OwnerId",
                table: "Audits",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PlanogramName",
                table: "Audits",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "PlanogramVersion",
                table: "Audits",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ResultJson",
                table: "Audits",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ReviewCases",
                table: "Audits",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "Audits",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SnapshotJson",
                table: "Audits",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SourceKey",
                table: "Audits",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Store",
                table: "Audits",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ActorId",
                table: "AuditCorrection",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "AuditCorrection",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "AuditCorrection",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Audits_OwnerId_Mode_CreatedAtUtc",
                table: "Audits",
                columns: new[] { "OwnerId", "Mode", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Audits_OwnerId_SourceKey",
                table: "Audits",
                columns: new[] { "OwnerId", "SourceKey" },
                unique: true,
                filter: "SourceKey <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Audits_OwnerId_Mode_CreatedAtUtc",
                table: "Audits");

            migrationBuilder.DropIndex(
                name: "IX_Audits_OwnerId_SourceKey",
                table: "Audits");

            migrationBuilder.DropColumn(
                name: "ConfirmedViolations",
                table: "Audits");

            migrationBuilder.DropColumn(
                name: "CurrentDetectionsJson",
                table: "Audits");

            migrationBuilder.DropColumn(
                name: "DetectionCount",
                table: "Audits");

            migrationBuilder.DropColumn(
                name: "HasCorrections",
                table: "Audits");

            migrationBuilder.DropColumn(
                name: "ModelName",
                table: "Audits");

            migrationBuilder.DropColumn(
                name: "ModelSha256",
                table: "Audits");

            migrationBuilder.DropColumn(
                name: "OriginalPercent",
                table: "Audits");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "Audits");

            migrationBuilder.DropColumn(
                name: "PlanogramName",
                table: "Audits");

            migrationBuilder.DropColumn(
                name: "PlanogramVersion",
                table: "Audits");

            migrationBuilder.DropColumn(
                name: "ResultJson",
                table: "Audits");

            migrationBuilder.DropColumn(
                name: "ReviewCases",
                table: "Audits");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "Audits");

            migrationBuilder.DropColumn(
                name: "SnapshotJson",
                table: "Audits");

            migrationBuilder.DropColumn(
                name: "SourceKey",
                table: "Audits");

            migrationBuilder.DropColumn(
                name: "Store",
                table: "Audits");

            migrationBuilder.DropColumn(
                name: "ActorId",
                table: "AuditCorrection");

            migrationBuilder.DropColumn(
                name: "Reason",
                table: "AuditCorrection");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "AuditCorrection");

            migrationBuilder.AlterColumn<int>(
                name: "PlanogramId",
                table: "Audits",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);
        }
    }
}
