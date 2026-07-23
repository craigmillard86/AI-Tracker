using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hap.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInitiativeDetailTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApprovalStatus",
                table: "initiatives",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Approver",
                table: "initiatives",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DataSensitivity",
                table: "initiatives",
                type: "text",
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<string>(
                name: "GovernanceNotes",
                table: "initiatives",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OversightModel",
                table: "initiatives",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UsesCogito",
                table: "initiatives",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<List<string>>(
                name: "models_providers",
                table: "initiatives",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<List<string>>(
                name: "regulatory_relevance",
                table: "initiatives",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<List<string>>(
                name: "vendors_tools",
                table: "initiatives",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.CreateTable(
                name: "initiative_nr_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InitiativeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Direction = table.Column<string>(type: "text", nullable: false),
                    Recurrence = table.Column<string>(type: "text", nullable: false),
                    AmountUsd = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ReferencedBySubmissionLineId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_initiative_nr_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_initiative_nr_lines_initiatives_InitiativeId",
                        column: x => x.InitiativeId,
                        principalTable: "initiatives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "initiative_stage_history",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InitiativeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Stage = table.Column<string>(type: "text", nullable: false),
                    PriorStage = table.Column<string>(type: "text", nullable: true),
                    EnteredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EnteredBy = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_initiative_stage_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_initiative_stage_history_initiatives_InitiativeId",
                        column: x => x.InitiativeId,
                        principalTable: "initiatives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "initiative_weekly_updates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InitiativeId = table.Column<Guid>(type: "uuid", nullable: false),
                    RagStatus = table.Column<string>(type: "text", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_initiative_weekly_updates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_initiative_weekly_updates_initiatives_InitiativeId",
                        column: x => x.InitiativeId,
                        principalTable: "initiatives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_initiative_nr_lines_InitiativeId",
                table: "initiative_nr_lines",
                column: "InitiativeId");

            migrationBuilder.CreateIndex(
                name: "IX_initiative_stage_history_InitiativeId",
                table: "initiative_stage_history",
                column: "InitiativeId");

            migrationBuilder.CreateIndex(
                name: "IX_initiative_stage_history_InitiativeId_EnteredAt",
                table: "initiative_stage_history",
                columns: new[] { "InitiativeId", "EnteredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_initiative_weekly_updates_InitiativeId",
                table: "initiative_weekly_updates",
                column: "InitiativeId");

            migrationBuilder.CreateIndex(
                name: "IX_initiative_weekly_updates_InitiativeId_CreatedAt",
                table: "initiative_weekly_updates",
                columns: new[] { "InitiativeId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "initiative_nr_lines");

            migrationBuilder.DropTable(
                name: "initiative_stage_history");

            migrationBuilder.DropTable(
                name: "initiative_weekly_updates");

            migrationBuilder.DropColumn(
                name: "ApprovalStatus",
                table: "initiatives");

            migrationBuilder.DropColumn(
                name: "Approver",
                table: "initiatives");

            migrationBuilder.DropColumn(
                name: "DataSensitivity",
                table: "initiatives");

            migrationBuilder.DropColumn(
                name: "GovernanceNotes",
                table: "initiatives");

            migrationBuilder.DropColumn(
                name: "OversightModel",
                table: "initiatives");

            migrationBuilder.DropColumn(
                name: "UsesCogito",
                table: "initiatives");

            migrationBuilder.DropColumn(
                name: "models_providers",
                table: "initiatives");

            migrationBuilder.DropColumn(
                name: "regulatory_relevance",
                table: "initiatives");

            migrationBuilder.DropColumn(
                name: "vendors_tools",
                table: "initiatives");
        }
    }
}
