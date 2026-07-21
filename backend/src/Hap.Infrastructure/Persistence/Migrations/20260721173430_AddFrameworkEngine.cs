using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hap.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFrameworkEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "frameworks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Owner = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_frameworks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "framework_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FrameworkId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    SourceRef = table.Column<string>(type: "text", nullable: true),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_framework_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_framework_versions_frameworks_FrameworkId",
                        column: x => x.FrameworkId,
                        principalTable: "frameworks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dimensions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FrameworkVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dimensions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_dimensions_framework_versions_FrameworkVersionId",
                        column: x => x.FrameworkVersionId,
                        principalTable: "framework_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "level_descriptors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DimensionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    LevelName = table.Column<string>(type: "text", nullable: false),
                    DescriptorText = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_level_descriptors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_level_descriptors_dimensions_DimensionId",
                        column: x => x.DimensionId,
                        principalTable: "dimensions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_dimensions_FrameworkVersionId_Key",
                table: "dimensions",
                columns: new[] { "FrameworkVersionId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_framework_versions_FrameworkId_VersionNumber",
                table: "framework_versions",
                columns: new[] { "FrameworkId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_frameworks_Key",
                table: "frameworks",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_level_descriptors_DimensionId_Level",
                table: "level_descriptors",
                columns: new[] { "DimensionId", "Level" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "level_descriptors");

            migrationBuilder.DropTable(
                name: "dimensions");

            migrationBuilder.DropTable(
                name: "framework_versions");

            migrationBuilder.DropTable(
                name: "frameworks");
        }
    }
}
