using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hap.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBuReporting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NOTE (drift catch-up, not a HAP-15 change): the scaffolder proposed an AddColumn for
            // "xmin" on "initiatives" here because the model snapshot never caught up to
            // InitiativeConfiguration's HAP-14 `e.Property<uint>("xmin").IsRowVersion();` (see that
            // class's doc comment — "Npgsql maps a `uint` property named `xmin` to Postgres's
            // always-present system column, so this needs NO migration and adds no DDL"). xmin is a
            // real Postgres system pseudo-column on every table already; an explicit ADD COLUMN for it
            // would fail outright ("column xmin already exists"). Omitted here, exactly as HAP-14's own
            // AddInitiativeDetailTracking migration omitted it. The model snapshot update this migration
            // carries (bringing HapDbContextModelSnapshot.cs's Initiative entity in sync with the
            // xmin config that has been live in code since HAP-14) is itself correct and kept.

            migrationBuilder.CreateTable(
                name: "bu_ai_dlc_declarations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    WeekOf = table.Column<DateOnly>(type: "date", nullable: false),
                    DeclaredLevel = table.Column<int>(type: "integer", nullable: false),
                    NextLevelExpectedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    RagStatus = table.Column<string>(type: "text", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    DeclaredBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bu_ai_dlc_declarations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_bu_ai_dlc_declarations_business_units_BusinessUnitId",
                        column: x => x.BusinessUnitId,
                        principalTable: "business_units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bu_monthly_metrics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Month = table.Column<DateOnly>(type: "date", nullable: false),
                    SorCalledByOtherApps = table.Column<string>(type: "text", nullable: true),
                    SubmittedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SupportCustomer = table.Column<string>(type: "jsonb", nullable: false),
                    SupportInternal = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bu_monthly_metrics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_bu_monthly_metrics_business_units_BusinessUnitId",
                        column: x => x.BusinessUnitId,
                        principalTable: "business_units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bu_ai_dlc_declarations_BusinessUnitId_WeekOf",
                table: "bu_ai_dlc_declarations",
                columns: new[] { "BusinessUnitId", "WeekOf" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bu_monthly_metrics_BusinessUnitId_Month",
                table: "bu_monthly_metrics",
                columns: new[] { "BusinessUnitId", "Month" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bu_ai_dlc_declarations");

            migrationBuilder.DropTable(
                name: "bu_monthly_metrics");
        }
    }
}
