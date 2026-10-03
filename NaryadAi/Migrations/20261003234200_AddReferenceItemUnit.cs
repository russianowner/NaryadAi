using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NaryadAi.Data;

#nullable disable

namespace NaryadAi.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261003234200_AddReferenceItemUnit")]
public partial class AddReferenceItemUnit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(name: "Unit", table: "ReferenceItems", type: "text", nullable: false, defaultValue: "");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "Unit", table: "ReferenceItems");
}
