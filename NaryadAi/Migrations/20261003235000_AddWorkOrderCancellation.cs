using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NaryadAi.Data;

#nullable disable

namespace NaryadAi.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261003235000_AddWorkOrderCancellation")]
public partial class AddWorkOrderCancellation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<DateTime>(
            name: "CancelledAt", table: "WorkOrders", type: "timestamp with time zone", nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "CancelledAt", table: "WorkOrders");
}
