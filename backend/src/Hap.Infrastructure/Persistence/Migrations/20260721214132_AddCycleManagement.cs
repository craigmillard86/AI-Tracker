using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hap.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCycleManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cycles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FrameworkVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    ContractorExclusionEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    OpensAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosesAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cycles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cycles_framework_versions_FrameworkVersionId",
                        column: x => x.FrameworkVersionId,
                        principalTable: "framework_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cycle_invitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CycleId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvitedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Excluded = table.Column<bool>(type: "boolean", nullable: false),
                    ExcludedReason = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cycle_invitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cycle_invitations_cycles_CycleId",
                        column: x => x.CycleId,
                        principalTable: "cycles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cycle_invitations_people_PersonId",
                        column: x => x.PersonId,
                        principalTable: "people",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cycle_late_overrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CycleId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    GrantedByPersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    GrantedByRole = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cycle_late_overrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cycle_late_overrides_cycles_CycleId",
                        column: x => x.CycleId,
                        principalTable: "cycles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cycle_late_overrides_people_GrantedByPersonId",
                        column: x => x.GrantedByPersonId,
                        principalTable: "people",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cycle_late_overrides_people_PersonId",
                        column: x => x.PersonId,
                        principalTable: "people",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cycle_invitations_CycleId_PersonId",
                table: "cycle_invitations",
                columns: new[] { "CycleId", "PersonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cycle_invitations_PersonId",
                table: "cycle_invitations",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_cycle_late_overrides_CycleId_PersonId",
                table: "cycle_late_overrides",
                columns: new[] { "CycleId", "PersonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cycle_late_overrides_GrantedByPersonId",
                table: "cycle_late_overrides",
                column: "GrantedByPersonId");

            migrationBuilder.CreateIndex(
                name: "IX_cycle_late_overrides_PersonId",
                table: "cycle_late_overrides",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_cycles_FrameworkVersionId_State",
                table: "cycles",
                columns: new[] { "FrameworkVersionId", "State" });

            // FR-054 DB-layer backstop (HAP-6 panel carry-forward, advisory A6): FrameworkVersion.Lock()
            // has been an in-memory/EF-only guard since HAP-6 — "nothing stops a raw SQL write to a
            // locked version's rows". Cycle.Open() (this story) is Lock()'s first real caller, so this
            // migration adds the DB trigger the HAP-6 panel deferred to here, mirroring the audit_log
            // append-only trigger from migration #1: once framework_versions."IsLocked" = true, any
            // further UPDATE/DELETE on that row, or any INSERT/UPDATE/DELETE against dimensions/
            // level_descriptors rows belonging to it, is rejected by Postgres itself — not just by the
            // domain guard (FrameworkVersion.EnsureMutable) every in-process write path already calls.
            //
            // L2 panel round-1 blocking fix (hap-code-reviewer, empirically proven): the first version
            // of this trigger checked only the NEW parent on an UPDATE (NEW is never null there), so a
            // raw SQL re-parent — UPDATE dimensions SET "FrameworkVersionId" = <unlocked> WHERE "Id" =
            // <dim-under-locked> — moved a row OUT of a locked version undetected (it checked where the
            // row was GOING, never where it came FROM), after which the row was freely mutable/deletable
            // under its new, unlocked parent. Fixed by checking BOTH the OLD parent's lock state (blocks
            // re-parenting a row away from a locked version, and blocks DELETE) and the NEW parent's lock
            // state (blocks re-parenting a row INTO a locked version, and blocks INSERT) — TG_OP gates
            // which of OLD/NEW is meaningful rather than relying on COALESCE, which silently prefers NEW
            // and was exactly the gap. Also adds the framework_versions BEFORE DELETE guard (advisory —
            // the FK Restrict from cycles/dimensions already prevents deleting a referenced version in
            // practice, but the trigger is now complete on its own terms).
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION hap_framework_version_locked_guard()
                RETURNS trigger AS $$
                DECLARE
                    old_locked boolean := false;
                    new_locked boolean := false;
                BEGIN
                    IF TG_TABLE_NAME = 'framework_versions' THEN
                        IF TG_OP = 'DELETE' THEN
                            IF OLD.""IsLocked"" THEN
                                RAISE EXCEPTION 'framework_versions.% is locked and cannot be deleted (FR-054)', OLD.""Id"";
                            END IF;
                            RETURN OLD;
                        END IF;
                        IF OLD.""IsLocked"" THEN
                            RAISE EXCEPTION 'framework_versions.% is locked and cannot be modified (FR-054)', OLD.""Id"";
                        END IF;
                        RETURN NEW;
                    END IF;

                    IF TG_TABLE_NAME = 'dimensions' THEN
                        IF TG_OP IN ('UPDATE', 'DELETE') THEN
                            SELECT ""IsLocked"" INTO old_locked FROM framework_versions
                                WHERE ""Id"" = OLD.""FrameworkVersionId"";
                        END IF;
                        IF TG_OP IN ('INSERT', 'UPDATE') THEN
                            SELECT ""IsLocked"" INTO new_locked FROM framework_versions
                                WHERE ""Id"" = NEW.""FrameworkVersionId"";
                        END IF;
                    ELSIF TG_TABLE_NAME = 'level_descriptors' THEN
                        IF TG_OP IN ('UPDATE', 'DELETE') THEN
                            SELECT fv.""IsLocked"" INTO old_locked FROM dimensions d
                                JOIN framework_versions fv ON fv.""Id"" = d.""FrameworkVersionId""
                                WHERE d.""Id"" = OLD.""DimensionId"";
                        END IF;
                        IF TG_OP IN ('INSERT', 'UPDATE') THEN
                            SELECT fv.""IsLocked"" INTO new_locked FROM dimensions d
                                JOIN framework_versions fv ON fv.""Id"" = d.""FrameworkVersionId""
                                WHERE d.""Id"" = NEW.""DimensionId"";
                        END IF;
                    END IF;

                    IF COALESCE(old_locked, false) OR COALESCE(new_locked, false) THEN
                        RAISE EXCEPTION '%.% belongs to a locked framework_version (old or new parent) and cannot be modified (FR-054)',
                            TG_TABLE_NAME, COALESCE(NEW.""Id"", OLD.""Id"");
                    END IF;

                    RETURN COALESCE(NEW, OLD);
                END;
                $$ LANGUAGE plpgsql;");
            migrationBuilder.Sql(@"
                CREATE TRIGGER framework_versions_locked_guard
                BEFORE UPDATE OR DELETE ON framework_versions
                FOR EACH ROW EXECUTE FUNCTION hap_framework_version_locked_guard();");
            migrationBuilder.Sql(@"
                CREATE TRIGGER dimensions_locked_guard
                BEFORE INSERT OR UPDATE OR DELETE ON dimensions
                FOR EACH ROW EXECUTE FUNCTION hap_framework_version_locked_guard();");
            migrationBuilder.Sql(@"
                CREATE TRIGGER level_descriptors_locked_guard
                BEFORE INSERT OR UPDATE OR DELETE ON level_descriptors
                FOR EACH ROW EXECUTE FUNCTION hap_framework_version_locked_guard();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS level_descriptors_locked_guard ON level_descriptors;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS dimensions_locked_guard ON dimensions;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS framework_versions_locked_guard ON framework_versions;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS hap_framework_version_locked_guard();");

            migrationBuilder.DropTable(
                name: "cycle_invitations");

            migrationBuilder.DropTable(
                name: "cycle_late_overrides");

            migrationBuilder.DropTable(
                name: "cycles");
        }
    }
}
