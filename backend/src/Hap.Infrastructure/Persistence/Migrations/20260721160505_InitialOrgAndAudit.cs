using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hap.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialOrgAndAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActorPersonId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "text", nullable: false),
                    SubjectPersonId = table.Column<Guid>(type: "uuid", nullable: true),
                    Detail = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_log", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "portfolios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_portfolios", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "groups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    PortfolioId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_groups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_groups_portfolios_PortfolioId",
                        column: x => x.PortfolioId,
                        principalTable: "portfolios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "business_units",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsOnboarded = table.Column<bool>(type: "boolean", nullable: false),
                    DirectorySource = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_units", x => x.Id);
                    table.ForeignKey(
                        name: "FK_business_units_groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "people",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalRef = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    JobTitle = table.Column<string>(type: "text", nullable: false),
                    ManagerPersonId = table.Column<Guid>(type: "uuid", nullable: true),
                    BusinessUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeType = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    OnLeave = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_people", x => x.Id);
                    table.ForeignKey(
                        name: "FK_people_business_units_BusinessUnitId",
                        column: x => x.BusinessUnitId,
                        principalTable: "business_units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_people_people_ManagerPersonId",
                        column: x => x.ManagerPersonId,
                        principalTable: "people",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "org_overrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Field = table.Column<string>(type: "text", nullable: false),
                    OriginalValue = table.Column<string>(type: "text", nullable: true),
                    OverrideValue = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_org_overrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_org_overrides_people_PersonId",
                        column: x => x.PersonId,
                        principalTable: "people",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "role_grants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    BusinessUnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    GrantedBy = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_grants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_role_grants_business_units_BusinessUnitId",
                        column: x => x.BusinessUnitId,
                        principalTable: "business_units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_role_grants_people_PersonId",
                        column: x => x.PersonId,
                        principalTable: "people",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_At",
                table: "audit_log",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_SubjectPersonId_At",
                table: "audit_log",
                columns: new[] { "SubjectPersonId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_business_units_Code",
                table: "business_units",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_business_units_GroupId",
                table: "business_units",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_groups_PortfolioId_Name",
                table: "groups",
                columns: new[] { "PortfolioId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_org_overrides_PersonId",
                table: "org_overrides",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_people_BusinessUnitId",
                table: "people",
                column: "BusinessUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_people_ExternalRef",
                table: "people",
                column: "ExternalRef",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_people_ManagerPersonId",
                table: "people",
                column: "ManagerPersonId");

            migrationBuilder.CreateIndex(
                name: "IX_portfolios_Name",
                table: "portfolios",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_role_grants_BusinessUnitId",
                table: "role_grants",
                column: "BusinessUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_role_grants_PersonId",
                table: "role_grants",
                column: "PersonId");

            // Append-only backstop at the database layer (FR-053). The application may only ever
            // INSERT into audit_log; any UPDATE or DELETE — issued through EF (Remove,
            // EntityState.Deleted, RemoveRange, ExecuteUpdate/ExecuteDelete, property-bag UPDATE)
            // or through raw SQL — is rejected by the database itself. This is the enforcement the
            // source-scan guard cannot make (it is evadable); the guard remains as an early signal.
            // Two triggers are needed: a row-level trigger for UPDATE/DELETE, and a statement-level
            // trigger for TRUNCATE (row triggers never fire on TRUNCATE — the mass-delete route).
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION hap_audit_log_append_only()
                RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION 'audit_log is append-only: % is not permitted', TG_OP;
                END;
                $$ LANGUAGE plpgsql;");
            migrationBuilder.Sql(@"
                CREATE TRIGGER audit_log_no_update_delete
                BEFORE UPDATE OR DELETE ON audit_log
                FOR EACH ROW EXECUTE FUNCTION hap_audit_log_append_only();");
            migrationBuilder.Sql(@"
                CREATE TRIGGER audit_log_no_truncate
                BEFORE TRUNCATE ON audit_log
                FOR EACH STATEMENT EXECUTE FUNCTION hap_audit_log_append_only();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS audit_log_no_truncate ON audit_log;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS audit_log_no_update_delete ON audit_log;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS hap_audit_log_append_only();");

            migrationBuilder.DropTable(
                name: "audit_log");

            migrationBuilder.DropTable(
                name: "org_overrides");

            migrationBuilder.DropTable(
                name: "role_grants");

            migrationBuilder.DropTable(
                name: "people");

            migrationBuilder.DropTable(
                name: "business_units");

            migrationBuilder.DropTable(
                name: "groups");

            migrationBuilder.DropTable(
                name: "portfolios");
        }
    }
}
