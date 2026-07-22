using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hap.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAssessments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "assessments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CycleId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModeratedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModeratedByPersonId = table.Column<Guid>(type: "uuid", nullable: true),
                    Unmoderated = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_assessments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_assessments_cycles_CycleId",
                        column: x => x.CycleId,
                        principalTable: "cycles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_assessments_people_PersonId",
                        column: x => x.PersonId,
                        principalTable: "people",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "assessment_scores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssessmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    DimensionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SelfScore = table.Column<int>(type: "integer", nullable: false),
                    SelfEvidence = table.Column<string>(type: "text", nullable: true),
                    ManagerScore = table.Column<int>(type: "integer", nullable: true),
                    ManagerComment = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_assessment_scores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_assessment_scores_assessments_AssessmentId",
                        column: x => x.AssessmentId,
                        principalTable: "assessments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_assessment_scores_dimensions_DimensionId",
                        column: x => x.DimensionId,
                        principalTable: "dimensions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_assessment_scores_AssessmentId_DimensionId",
                table: "assessment_scores",
                columns: new[] { "AssessmentId", "DimensionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_assessment_scores_DimensionId",
                table: "assessment_scores",
                column: "DimensionId");

            migrationBuilder.CreateIndex(
                name: "IX_assessments_CycleId_PersonId",
                table: "assessments",
                columns: new[] { "CycleId", "PersonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_assessments_PersonId",
                table: "assessments",
                column: "PersonId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assessment_scores");

            migrationBuilder.DropTable(
                name: "assessments");
        }
    }
}
