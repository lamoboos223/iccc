using Iccc.Application.Approval;
using Iccc.Application.Assistant;
using Iccc.Application.LoadTesting;
using Iccc.Application.Safety;
using Iccc.Domain;

namespace Iccc.Application.Tests;

public class PlatformRulesTests
{
    [Fact]
    public void Workload_must_name_its_metric_and_keeps_users_distinct_from_rps()
    {
        var errors = WorkloadRules.Validate(new WorkloadDraft
        {
            Metric = WorkloadMetricKind.ConcurrentUsers,
            TargetValue = 500,
            SustainSeconds = 60,
            VirtualUsers = 500
        });

        Assert.Contains(errors, error => error.Contains("not requests per second", StringComparison.Ordinal));
    }

    [Fact]
    public void Endpoint_weights_must_sum_to_100()
    {
        var errors = WorkloadRules.Validate(new WorkloadDraft
        {
            Metric = WorkloadMetricKind.RequestsPerSecond,
            TargetValue = 1000,
            SustainSeconds = 60,
            EndpointWeights = [40, 40]
        });

        Assert.Contains(errors, error => error.Contains("sum to 100", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_and_unlisted_hosts_are_rejected()
    {
        var production = LoadTestGuard.Evaluate(Guard() with { EnvironmentKind = EnvironmentKind.Production });
        var stranger = LoadTestGuard.Evaluate(Guard() with { TargetUrl = "https://unknown.example/api" });
        var metadata = LoadTestGuard.Evaluate(Guard() with { TargetUrl = "http://169.254.169.254/latest" });

        Assert.False(production.Allowed);
        Assert.False(stranger.Allowed);
        Assert.False(metadata.Allowed);
    }

    [Fact]
    public void High_rate_requires_authorization_and_inline_secrets_are_rejected()
    {
        var unauthorized = LoadTestGuard.Evaluate(Guard() with { RequestsPerSecond = 5000, ExplicitlyAuthorized = false });
        var secret = LoadTestGuard.Evaluate(Guard() with { ScriptBody = "const password = 'hunter2';" });
        var allowed = LoadTestGuard.Evaluate(Guard());

        Assert.False(unauthorized.Allowed);
        Assert.False(secret.Allowed);
        Assert.True(allowed.Allowed);
    }

    [Fact]
    public void Generated_k6_script_targets_arrival_rate_and_does_not_embed_a_token()
    {
        var script = K6ScriptGenerator.Generate(new K6ScriptRequest
        {
            BaseUrl = "https://payments-test.internal",
            TargetRps = 1000,
            RampUpSeconds = 30,
            SustainSeconds = 120,
            RampDownSeconds = 15,
            Endpoints = [new K6Endpoint("GET", "/health", 20), new K6Endpoint("POST", "/payments", 80)],
            MaxP95LatencyMs = 200,
            AuthenticationRequired = true,
            PayloadBytes = 64
        });

        Assert.Contains("ramping-arrival-rate", script, StringComparison.Ordinal);
        Assert.Contains("not the workload target", script, StringComparison.Ordinal);
        Assert.Contains("__ENV.ICCC_AUTH_TOKEN", script, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", script, StringComparison.Ordinal);
        Assert.Contains("/payments", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Approval_cannot_skip_ahead_or_certify_a_blocked_calculation()
    {
        Assert.False(ApprovalWorkflow.CanTransition(RequestStatus.Draft, RequestStatus.Approved, true, true, true, true, false));
        Assert.False(ApprovalWorkflow.CanTransition(RequestStatus.PendingApproval, RequestStatus.Approved, true, true, false, true, true));
        Assert.True(ApprovalWorkflow.CanTransition(RequestStatus.PendingApproval, RequestStatus.Approved, true, true, true, false, false));
        Assert.True(ApprovalWorkflow.CanTransition(RequestStatus.PendingApproval, RequestStatus.Approved, true, true, false, true, false));
        Assert.False(ApprovalWorkflow.CanTransition(RequestStatus.PendingApproval, RequestStatus.Approved, false, true, true, true, false));
    }

    [Fact]
    public void Narrative_guard_drops_numbers_the_engine_did_not_produce()
    {
        var filtered = NarrativeGuard.Apply("You need 32 vCPU. The engine calculated 8 vCPU.", [8]);

        Assert.Contains("removed", filtered, StringComparison.Ordinal);
        Assert.Contains("8 vCPU", filtered, StringComparison.Ordinal);
        Assert.DoesNotContain("32", filtered, StringComparison.Ordinal);
    }

    [Fact]
    public void Assistant_will_not_calculate_or_execute_without_measurements_and_authorization()
    {
        var missing = AssistantPlanner.Plan(new AssistantState
        {
            ServiceId = Guid.NewGuid(),
            ServiceFound = true,
            ArchitectureComplete = true,
            WorkloadDefined = true,
            WorkloadUsesRequestsPerSecond = true,
            HasExecution = false,
            AuthorizeTests = false
        }, "Estimate the required infrastructure for 100K RPS.");

        var ready = AssistantPlanner.Plan(new AssistantState
        {
            ServiceId = Guid.NewGuid(),
            ServiceFound = true,
            ArchitectureComplete = true,
            WorkloadDefined = true,
            WorkloadUsesRequestsPerSecond = true,
            HasExecution = true,
            MeasurementUsable = true
        }, "Estimate the required infrastructure.");

        Assert.False(missing.MayCalculate);
        Assert.False(missing.MayExecuteTest);
        Assert.DoesNotContain("execute_load_test", missing.Tools);
        Assert.Contains("validate_load_test", missing.Tools);
        Assert.True(ready.MayCalculate);
        Assert.Contains("calculate_infrastructure_capacity", ready.Tools);
        Assert.DoesNotContain("execute_load_test", ready.Tools);
    }

    private static LoadTestGuardRequest Guard() => new()
    {
        EnvironmentKind = EnvironmentKind.Test,
        LoadTestAllowed = true,
        TargetUrl = "https://payments-test.internal/api",
        AllowlistHosts = ["payments-test.internal"],
        RequestsPerSecond = 100,
        DurationSeconds = 60,
        MaxRequestsPerSecond = 20000,
        MaxDurationSeconds = 3600,
        HighLoadAuthorizationThreshold = 1000,
        ExplicitlyAuthorized = true
    };
}
