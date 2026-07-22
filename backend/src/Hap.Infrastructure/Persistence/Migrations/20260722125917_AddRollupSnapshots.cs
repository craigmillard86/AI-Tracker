using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hap.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRollupSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NOTE: the scaffolder wanted to AddColumn "xmin" (xid, rowVersion) on `assessments` because
            // the HEAD model snapshot never recorded HAP-9's concurrency token. `xmin` is a PostgreSQL
            // SYSTEM column (Npgsql maps the uint rowversion property to it and emits NO DDL — see
            // AssessmentEntityConfiguration); `ALTER TABLE ... ADD COLUMN xmin` is in fact rejected by
            // Postgres as a reserved system-column name. So that operation is deliberately omitted here —
            // this migration only introduces `rollup_snapshots`. The regenerated model snapshot now carries
            // the token, so no future migration re-proposes it.

            migrationBuilder.CreateTable(
                name: "rollup_snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CycleId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrgNodeType = table.Column<string>(type: "text", nullable: false),
                    OrgNodeRef = table.Column<Guid>(type: "uuid", nullable: true),
                    N = table.Column<int>(type: "integer", nullable: false),
                    PerDimensionMean = table.Column<string>(type: "jsonb", nullable: false),
                    FloorLevelDistribution = table.Column<string>(type: "jsonb", nullable: false),
                    CompletionDenominator = table.Column<int>(type: "integer", nullable: false),
                    CompletionPct = table.Column<double>(type: "double precision", nullable: false),
                    UnmoderatedPct = table.Column<double>(type: "double precision", nullable: false),
                    CalibrationDelta = table.Column<string>(type: "jsonb", nullable: false),
                    Suppressed = table.Column<bool>(type: "boolean", nullable: false),
                    SuppressionReason = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rollup_snapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_rollup_snapshots_cycles_CycleId",
                        column: x => x.CycleId,
                        principalTable: "cycles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // UNIQUE (not a plain index) — one snapshot per (cycle, node type, node ref). `cycles` carries no
            // concurrency token, so two concurrent CloseAsync could each insert a full snapshot set; this
            // constraint makes the racing second insert fail and its transaction roll back, instead of
            // leaving duplicate frozen rows the append-only triggers would make permanently undeletable.
            // NULLS NOT DISTINCT so the single AllHig row (node ref null) is deduplicated too.
            migrationBuilder.CreateIndex(
                name: "IX_rollup_snapshots_CycleId_OrgNodeType_OrgNodeRef",
                table: "rollup_snapshots",
                columns: new[] { "CycleId", "OrgNodeType", "OrgNodeRef" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            // Append-only backstop at the database layer (research D2/D4, FR-071). A rollup snapshot is
            // frozen at cycle close and MUST never be updated, deleted, or truncated — that is what makes
            // history impossible to retro-expose or silently unsuppress. Mirrors HAP-3's audit_log guard
            // EXACTLY, adding the piece HAP-7 explicitly deferred to "the next story adding an append-only
            // table" (that is this one): the row-level trigger does NOT fire on TRUNCATE, so a SEPARATE
            // statement-level BEFORE TRUNCATE trigger is required to close the mass-delete route.
            //
            // HONEST SCOPE (round-1 review): these are ordinary ("origin") triggers, and the local app role
            // `hap` is the database OWNER/superuser — so they are NOT an absolute guarantee, exactly the same
            // accepted residual as the HAP-3 audit_log and HAP-7 framework-lock triggers: an owner CAN drop a
            // trigger or SET session_replication_role='replica' to bypass them (which is precisely how the
            // test harness resets the schema between fixtures). What they DO guarantee is that no ordinary
            // INSERT-path bug and no HTTP-reachable code can mutate/delete/truncate a snapshot — there is no
            // application path that sets session_replication_role or issues UPDATE/DELETE/TRUNCATE here. The
            // real hardening (running the app under a NON-owner role that cannot bypass) is owed before real
            // data and is tracked with the same G1 obligation as the other append-only tables; it is out of
            // scope for this synthetic-data build.
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION hap_rollup_snapshot_append_only()
                RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION 'rollup_snapshots is append-only: % is not permitted', TG_OP;
                END;
                $$ LANGUAGE plpgsql;");
            migrationBuilder.Sql(@"
                CREATE TRIGGER rollup_snapshots_no_update_delete
                BEFORE UPDATE OR DELETE ON rollup_snapshots
                FOR EACH ROW EXECUTE FUNCTION hap_rollup_snapshot_append_only();");
            migrationBuilder.Sql(@"
                CREATE TRIGGER rollup_snapshots_no_truncate
                BEFORE TRUNCATE ON rollup_snapshots
                FOR EACH STATEMENT EXECUTE FUNCTION hap_rollup_snapshot_append_only();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS rollup_snapshots_no_truncate ON rollup_snapshots;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS rollup_snapshots_no_update_delete ON rollup_snapshots;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS hap_rollup_snapshot_append_only();");

            migrationBuilder.DropTable(
                name: "rollup_snapshots");
        }
    }
}
