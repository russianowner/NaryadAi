using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NaryadAi.Migrations
{
    /// <inheritdoc />
    public partial class UpdateWorkOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CloseComment",
                table: "WorkOrders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CloseFaultCode",
                table: "WorkOrders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CloseWorksDone",
                table: "WorkOrders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreationComment",
                table: "WorkOrders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "Deadline",
                table: "WorkOrders",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "Location",
                table: "WorkOrders",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "MasterId",
                table: "WorkOrders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Number",
                table: "WorkOrders",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PhotoAfterPath",
                table: "WorkOrders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhotoBeforePath",
                table: "WorkOrders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "WorkOrders",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_MasterId",
                table: "WorkOrders",
                column: "MasterId");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkOrders_Employees_MasterId",
                table: "WorkOrders",
                column: "MasterId",
                principalTable: "Employees",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkOrders_Employees_MasterId",
                table: "WorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_MasterId",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "CloseComment",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "CloseFaultCode",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "CloseWorksDone",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "CreationComment",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "Deadline",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "Location",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "MasterId",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "Number",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "PhotoAfterPath",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "PhotoBeforePath",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "WorkOrders");
        }
    }
}
