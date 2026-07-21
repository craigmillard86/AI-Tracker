using Hap.Api;
using Hap.Infrastructure;
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

builder.Services.AddHapInfrastructure(snapshotPath);

var app = builder.Build();

// Liveness only — deliberately does not touch the database so the container is
// reported healthy before migrations/seed have run.
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.MapAdminEndpoints();

app.Run();

// Exposed so Hap.Api.Tests can drive the app through WebApplicationFactory.
public partial class Program { }
