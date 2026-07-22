using Hap.Api;
using Hap.Api.Authorization;
using Hap.Api.Identity;
using Hap.Infrastructure;
using Hap.Infrastructure.Frameworks;
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

builder.Services.AddHapInfrastructure(snapshotPath, frameworkDefinitionPath);

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

app.Run();

// Exposed so Hap.Api.Tests can drive the app through WebApplicationFactory.
public partial class Program { }
