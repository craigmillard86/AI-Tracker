using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hap.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInitiativeRegister : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "harris_categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    GroupReported = table.Column<bool>(type: "boolean", nullable: false),
                    CustomerDeployed = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_harris_categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "harris_stage_map",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InternalStage = table.Column<string>(type: "text", nullable: false),
                    HarrisStage = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_harris_stage_map", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "initiatives",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    SponsorPersonId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerPersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByPersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegisteredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    AiDlcLevel = table.Column<int>(type: "integer", nullable: false),
                    functions_affected = table.Column<List<string>>(type: "text[]", nullable: false),
                    dimensions_advanced = table.Column<List<string>>(type: "text[]", nullable: false),
                    CurrentStage = table.Column<string>(type: "text", nullable: false),
                    RagStatus = table.Column<string>(type: "text", nullable: false),
                    LastUpdateAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CustomersInProduction = table.Column<int>(type: "integer", nullable: true),
                    RiskTier = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_initiatives", x => x.Id);
                    table.ForeignKey(
                        name: "FK_initiatives_business_units_BusinessUnitId",
                        column: x => x.BusinessUnitId,
                        principalTable: "business_units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_initiatives_harris_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "harris_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_harris_categories_Key",
                table: "harris_categories",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_harris_stage_map_InternalStage",
                table: "harris_stage_map",
                column: "InternalStage",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_initiatives_AiDlcLevel",
                table: "initiatives",
                column: "AiDlcLevel");

            migrationBuilder.CreateIndex(
                name: "IX_initiatives_BusinessUnitId_CurrentStage",
                table: "initiatives",
                columns: new[] { "BusinessUnitId", "CurrentStage" });

            migrationBuilder.CreateIndex(
                name: "IX_initiatives_CategoryId",
                table: "initiatives",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_initiatives_RiskTier",
                table: "initiatives",
                column: "RiskTier");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "harris_stage_map");

            migrationBuilder.DropTable(
                name: "initiatives");

            migrationBuilder.DropTable(
                name: "harris_categories");
        }
    }
}
