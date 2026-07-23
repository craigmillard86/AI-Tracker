using Hap.Api;
using Hap.Api.Authorization;
using Hap.Api.Identity;
using Hap.Api.Notifications;
using Hap.Infrastructure;
using Hap.Infrastructure.Email;
using Hap.Infrastructure.Frameworks;
using Hap.Infrastructure.Notifications;
using Hap.Infrastructure.Register;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// The visibility seam and assessment services attach in later stories. HAP-3 wires the org
// model: a DbContext, the directory importer, the audit writer, and the override service.
var connectionString =
    builder.Configuration.GetConnectionString("Hap")
    ?? Environment.GetEnvironmentVariable("HAP_DB_CONNECTION")
    ?? "Host=localhost;Port=5432;Database=hap;Username=hap;Password=hap";

builder.Services.AddDbContext<HapDbContext>(options => options.UseNpgsql(connectionString));

// Path to the deterministic synthetic directory snapshot (scripts/synth/generate.sh output).
// Configurable so docker-compose and tests can point it wherever the artefact lives.
var snapshotPath =
    builder.Configuration["Hap:DirectorySnapshotPath"]
    ?? Environment.GetEnvironmentVariable("HAP_DIRECTORY_SNAPSHOT")
    ?? Path.Combine(AppContext.BaseDirectory, "directory.json");

// Path to the seeded framework definition JSON (docs/frameworks/ai-maturity-sdlc.v1.json).
// Same override-then-fallback shape as the directory snapshot above (HAP-6, FR-001).
var frameworkDefinitionPath =
    builder.Configuration["Hap:FrameworkDefinitionPath"]
    ?? Environment.GetEnvironmentVariable("HAP_FRAMEWORK_DEFINITION")
    ?? FrameworkDefinitionLocator.ResolveDefaultPath();

// Path to the seeded Harris taxonomy JSON (docs/frameworks/harris-taxonomy.v1.json). Same
// override-then-fallback shape as the framework definition above (HAP-13, FR-027/FR-064).
var harrisTaxonomyDefinitionPath =
    builder.Configuration["Hap:HarrisTaxonomyDefinitionPath"]
    ?? Environment.GetEnvironmentVariable("HAP_HARRIS_TAXONOMY_DEFINITION")
    ?? HarrisTaxonomyDefinitionLocator.ResolveDefaultPath();

// Base URL for mailpit's REST API (HAP-18, FR-037) — the durable "already sent today" record
// MailpitSentMailLedger queries (see its class doc: mailpit's own message store IS the ledger, no
// sent-log table). Same override-then-fallback shape as the paths above; docker-compose's api
// service sets Mailpit__ApiBaseUrl to the container DNS name.
var mailpitApiBaseUrl =
    builder.Configuration["Mailpit:ApiBaseUrl"]
    ?? Environment.GetEnvironmentVariable("HAP_MAILPIT_API")
    ?? "http://mailpit:8025";

builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));

builder.Services.AddHapInfrastructure(
    snapshotPath, frameworkDefinitionPath, harrisTaxonomyDefinitionPath, mailpitApiBaseUrl);

// Path to the seed-users file (scripts/synth/generate.sh output) the local dev provider's
// role-picker reads (FR-055). Same configurable-path convention as the directory snapshot above.
var seedUsersPath =
    builder.Configuration["Hap:SeedUsersPath"]
    ?? Environment.GetEnvironmentVariable("HAP_SEED_USERS")
    ?? Path.Combine(AppContext.BaseDirectory, "seed-users.json");

builder.Services.AddHapIdentity(seedUsersPath);

// The visibility seam (CLAUDE.md §2). Foundation services only for now; the assessment-read gateway
// comes online with HAP-8's DbSet-backed store.
builder.Services.AddHapAuthorization();

// Resolves each BU's notification recipient ("BU lead") from an explicit BU-anchored BuDelegate grant —
// the same structural anchor the visibility seam (AssessmentReads.ClassifyReader) uses, so a BU-lead
// summary/escalation never reaches anyone the seam would deny a read of that BU's data. Deliberately NOT
// HierarchyRoleResolver's depth-from-root label, which mislabels a stranger as a BU's lead on a
// non-uniform tree (Q-014; HAP-18 QA Findings 1 & 2). See RoleGrantBuLeadResolver's class doc.
builder.Services.AddScoped<IBuLeadResolver, RoleGrantBuLeadResolver>();

// FR-061 cycle reminders/escalations (HAP-18, L3). The cadence is configuration, not a schema column
// (QUESTIONS.md Q-031): "days before close" is measured against OpensAt + a configured cycle length.
// The job lives in Hap.Api (not Hap.Infrastructure like the FR-037 job) because it consumes the seam's
// sanctioned state-only non-responder read and the chain resolver, both Hap.Api types.
builder.Services.Configure<NotificationCadenceOptions>(
    builder.Configuration.GetSection(NotificationCadenceOptions.SectionName));
builder.Services.AddScoped<CycleReminderJob>();

var app = builder.Build();

// Liveness only — deliberately does not touch the database so the container is
// reported healthy before migrations/seed have run.
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.UseAuthentication();
app.UseAuthorization();

// Every /api/** route requires an authenticated session (HAP-4 acceptance bar); /auth/** and
// /healthz stay outside this group so sign-in itself is reachable anonymously.
var api = app.MapGroup("/api").RequireAuthorization();

app.MapIdentityEndpoints(api);
api.MapAdminEndpoints();
api.MapFrameworkEndpoints();
api.MapCycleEndpoints();
api.MapAssessmentEndpoints();
api.MapTeamEndpoints();
api.MapRollupEndpoints();
api.MapRegisterEndpoints();
api.MapBuReportingEndpoints();

app.Run();

// Exposed so Hap.Api.Tests can drive the app through WebApplicationFactory.
public partial class Program { }
