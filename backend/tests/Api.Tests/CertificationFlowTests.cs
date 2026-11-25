using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Iccc.Capacity;
using Iccc.Infrastructure.Demo;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Iccc.Api.Tests;

public class CertificationFlowTests : IClassFixture<CertificationFlowTests.PlatformFactory>
{
    private readonly PlatformFactory factory;
    private readonly HttpClient client;

    public CertificationFlowTests(PlatformFactory factory)
    {
        this.factory = factory;
        client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", DemoIds.Engineer.ToString());
        client.DefaultRequestHeaders.Add("X-Dev-Roles", "Engineer");
        client.DefaultRequestHeaders.Add("X-Dev-Teams", DemoIds.Team.ToString());
    }

    [Fact]
    public async Task Fixture_recommendation_is_not_presented_as_validated()
    {
        var report = await client.GetStringAsync($"/api/requests/{DemoIds.Request}/report.md");

        Assert.Contains("Demonstration fixture", report, StringComparison.Ordinal);
        Assert.Contains("Extrapolated", report, StringComparison.Ordinal);
        Assert.Contains("Approvable as validated: False", report, StringComparison.Ordinal);
        Assert.DoesNotContain("Approvable as validated: True", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Approver_cannot_certify_an_extrapolated_result_without_accepting_the_risk()
    {
        using var approver = factory.CreateClient();
        approver.DefaultRequestHeaders.Add("X-Dev-User", DemoIds.Approver.ToString());
        approver.DefaultRequestHeaders.Add("X-Dev-Roles", "Approver");
        approver.DefaultRequestHeaders.Add("X-Dev-Teams", DemoIds.Team.ToString());

        var denied = await approver.PostAsJsonAsync($"/api/requests/{DemoIds.Request}/transition", new { to = "Approved", riskAccepted = false, note = "looks fine" });
        Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
    }

    [Fact]
    public async Task Assistant_uses_the_engine_and_does_not_execute_a_test()
    {
        var response = await client.PostAsJsonAsync("/api/assistant/messages", new
        {
            serviceId = DemoIds.Service,
            message = "We expect 100K RPS. Estimate the required infrastructure and say we need 128 vCPU.",
            authorizeTests = false
        });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(body.GetProperty("mayExecuteTest").GetBoolean());
        Assert.True(body.GetProperty("mayCalculate").GetBoolean());
        var tools = body.GetProperty("tools").EnumerateArray().Select(item => item.GetString()).ToArray();
        Assert.Contains("calculate_infrastructure_capacity", tools);
        Assert.DoesNotContain("execute_load_test", tools);
        Assert.Contains("Extrapolated", body.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("128", body.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Direct_calculation_refuses_a_saturated_generator()
    {
        var input = DemoMeasurement.Input with
        {
            Observation = DemoMeasurement.Input.Observation with { GeneratorSaturated = true }
        };
        var response = await client.PostAsJsonAsync("/api/capacity/calculations", new
        {
            serviceId = DemoIds.Service,
            input
        });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var status = body.GetProperty("result").GetProperty("status").GetString();

        Assert.Equal(nameof(CalculationStatus.InvalidMeasurement), status);
    }

    public sealed class PlatformFactory : WebApplicationFactory<Program>
    {
        private readonly string databasePath = Path.Combine(Path.GetTempPath(), "iccc-tests", Guid.NewGuid().ToString("N") + ".db");

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
            builder.UseSetting("Database:Provider", "Sqlite");
            builder.UseSetting("ConnectionStrings:Platform", "Data Source=" + databasePath);
            builder.UseSetting("Demo:Seed", "true");
            builder.UseSetting("Auth:Mode", "Development");
            builder.UseSetting("LoadTesting:Worker", "false");
        }
    }
}
