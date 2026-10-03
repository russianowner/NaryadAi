using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NaryadAi.Migrations
{
    /// <inheritdoc />
    public partial class UpdateModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Section",
                table: "Equipments",
                newName: "Type");

            migrationBuilder.AddColumn<string>(
                name: "Criticality",
                table: "Equipments",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Location",
                table: "Equipments",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Criticality",
                table: "Equipments");

            migrationBuilder.DropColumn(
                name: "Location",
                table: "Equipments");

            migrationBuilder.RenameColumn(
                name: "Type",
                table: "Equipments",
                newName: "Section");
        }
    }
}
