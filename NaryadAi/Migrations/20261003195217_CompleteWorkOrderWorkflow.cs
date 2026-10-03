using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NaryadAi.Migrations
{
    /// <inheritdoc />
    public partial class CompleteWorkOrderWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AcceptedAt",
                table: "WorkOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AiReviewStartedAt",
                table: "WorkOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ClosedAt",
                table: "WorkOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAt",
                table: "WorkOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PausedAt",
                table: "WorkOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "QueuedAt",
                table: "WorkOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RejectedAt",
                table: "WorkOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReworkRequestedAt",
                table: "WorkOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SiteId",
                table: "WorkOrders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartedAt",
                table: "WorkOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SiteId",
                table: "Equipments",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AiEvaluations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WorkOrderId = table.Column<int>(type: "integer", nullable: false),
                    Verdict = table.Column<string>(type: "text", nullable: false),
                    Score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    Explanation = table.Column<string>(type: "text", nullable: false),
                    MasterApproved = table.Column<bool>(type: "boolean", nullable: true),
                    MasterComment = table.Column<string>(type: "text", nullable: true),
                    EvaluatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiEvaluations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiEvaluations_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MaterialWriteOffs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WorkOrderId = table.Column<int>(type: "integer", nullable: false),
                    Material = table.Column<string>(type: "text", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    Unit = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialWriteOffs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaterialWriteOffs_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Sites",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sites", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WorkOrderId = table.Column<int>(type: "integer", nullable: false),
                    ActorId = table.Column<int>(type: "integer", nullable: true),
                    ActorName = table.Column<string>(type: "text", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Comment = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkOrderEvents_Employees_ActorId",
                        column: x => x.ActorId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WorkOrderEvents_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderPhotos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WorkOrderId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    FilePath = table.Column<string>(type: "text", nullable: false),
                    Sha256 = table.Column<string>(type: "text", nullable: false),
                    CapturedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AuthorId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkOrderPhotos_Employees_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WorkOrderPhotos_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_Number",
                table: "WorkOrders",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_SiteId",
                table: "WorkOrders",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_Status_Deadline",
                table: "WorkOrders",
                columns: new[] { "Status", "Deadline" });

            migrationBuilder.CreateIndex(
                name: "IX_Equipments_SiteId",
                table: "Equipments",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_AiEvaluations_WorkOrderId",
                table: "AiEvaluations",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialWriteOffs_WorkOrderId",
                table: "MaterialWriteOffs",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Sites_Name",
                table: "Sites",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderEvents_ActorId",
                table: "WorkOrderEvents",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderEvents_WorkOrderId_OccurredAt",
                table: "WorkOrderEvents",
                columns: new[] { "WorkOrderId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderPhotos_AuthorId",
                table: "WorkOrderPhotos",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderPhotos_WorkOrderId",
                table: "WorkOrderPhotos",
                column: "WorkOrderId");

            migrationBuilder.AddForeignKey(
                name: "FK_Equipments_Sites_SiteId",
                table: "Equipments",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkOrders_Sites_SiteId",
                table: "WorkOrders",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Equipments_Sites_SiteId",
                table: "Equipments");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkOrders_Sites_SiteId",
                table: "WorkOrders");

            migrationBuilder.DropTable(
                name: "AiEvaluations");

            migrationBuilder.DropTable(
                name: "MaterialWriteOffs");

            migrationBuilder.DropTable(
                name: "Sites");

            migrationBuilder.DropTable(
                name: "WorkOrderEvents");

            migrationBuilder.DropTable(
                name: "WorkOrderPhotos");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_Number",
                table: "WorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_SiteId",
                table: "WorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_Status_Deadline",
                table: "WorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_Equipments_SiteId",
                table: "Equipments");

            migrationBuilder.DropColumn(
                name: "AcceptedAt",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "AiReviewStartedAt",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "ClosedAt",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "PausedAt",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "QueuedAt",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "RejectedAt",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "ReworkRequestedAt",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "StartedAt",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "Equipments");
        }
    }
}
