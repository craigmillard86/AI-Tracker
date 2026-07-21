using Hap.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// The visibility seam and domain services attach in later stories. For the
// scaffold the API needs only a wired DbContext (no queries yet) and a
// dependency-free liveness probe so docker-compose can report health.
var connectionString =
    builder.Configuration.GetConnectionString("Hap")
    ?? Environment.GetEnvironmentVariable("HAP_DB_CONNECTION")
    ?? "Host=localhost;Port=5432;Database=hap;Username=hap;Password=hap";

builder.Services.AddDbContext<HapDbContext>(options => options.UseNpgsql(connectionString));

var app = builder.Build();

// Liveness only — deliberately does not touch the database so the container is
// reported healthy before migrations/seed have run.
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.Run();

// Exposed so Hap.Api.Tests can drive the app through WebApplicationFactory.
public partial class Program { }
