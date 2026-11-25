using System.Security.Claims;
using System.Text.Json.Serialization;
using Iccc.Api;
using Iccc.Application.Abstractions;
using Iccc.Infrastructure.Demo;
using Iccc.Infrastructure.LoadTesting;
using Iccc.Infrastructure.Persistence;
using Iccc.Infrastructure.Telemetry;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
if (builder.Environment.IsProduction() && builder.Configuration["Auth:Mode"] == "Development")
{
    throw new InvalidOperationException("Development authentication cannot be used in Production.");
}

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddOpenApi();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.AllowAnyHeader().AllowAnyMethod().WithOrigins("http://localhost:5173")));

var databaseProvider = builder.Configuration["Database:Provider"] ?? "Postgres";
var connectionString = builder.Configuration.GetConnectionString("Platform")
    ?? "Host=localhost;Port=5432;Database=iccc;Username=iccc;Password=iccc";
builder.Services.AddDbContext<PlatformDbContext>(options =>
{
    if (databaseProvider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
    {
        options.UseSqlite(connectionString);
    }
    else
    {
        options.UseNpgsql(connectionString);
    }
});

builder.Services.AddHttpClient<ITelemetryProvider, PrometheusTelemetryProvider>();
builder.Services.AddSingleton<ILlmProvider, DisabledLlmProvider>();
builder.Services.AddSingleton<ILoadTestEngine>(_ => new K6LoadTestEngine(
    builder.Configuration["LoadTesting:K6Path"] ?? "k6",
    int.TryParse(builder.Configuration["LoadTesting:SingleGeneratorMaxRps"], out var max) ? max : 5000));
builder.Services.AddHostedService<LoadTestWorker>();

var otlp = builder.Configuration["OpenTelemetry:Endpoint"];
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("iccc-api"))
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation();
        if (!string.IsNullOrWhiteSpace(otlp))
        {
            tracing.AddOtlpExporter(options => options.Endpoint = new Uri(otlp));
        }
    })
    .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation());

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors();
}

app.Use(async (context, next) =>
{
    if (!string.Equals(app.Configuration["Auth:Mode"], "Development", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "OIDC is not configured. Set Auth:Mode to an OIDC authority before exposing this API." });
        return;
    }

    var roles = (context.Request.Headers["X-Dev-Roles"].FirstOrDefault() ?? "Engineer").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var teams = (context.Request.Headers["X-Dev-Teams"].FirstOrDefault() ?? DemoIds.Team.ToString()).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var user = context.Request.Headers["X-Dev-User"].FirstOrDefault() ?? DemoIds.Engineer.ToString();
    var claims = new List<Claim> { new("uid", user), new(ClaimTypes.Name, user) };
    claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
    claims.AddRange(teams.Select(team => new Claim("team", team)));
    context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Development"));
    await next();
});

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapPlatform();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
    if (db.Database.GetMigrations().Any())
    {
        await db.Database.MigrateAsync();
    }
    else
    {
        await db.Database.EnsureCreatedAsync();
    }

    if (builder.Configuration.GetValue("Demo:Seed", false))
    {
        await DemoSeed.SeedAsync(db, CancellationToken.None);
    }
}

app.Run();

public partial class Program;
