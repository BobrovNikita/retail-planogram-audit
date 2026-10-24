using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailVision.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CatalogEditing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "Products",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "SeedMarker",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeedMarker", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ModelClassMappings_ModelVersionId_ProductId",
                table: "ModelClassMappings",
                columns: new[] { "ModelVersionId", "ProductId" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ModelClass_Index",
                table: "ModelClassMappings",
                sql: "ClassIndex >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SeedMarker");

            migrationBuilder.DropIndex(
                name: "IX_ModelClassMappings_ModelVersionId_ProductId",
                table: "ModelClassMappings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ModelClass_Index",
                table: "ModelClassMappings");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "Products");
        }
    }
}
