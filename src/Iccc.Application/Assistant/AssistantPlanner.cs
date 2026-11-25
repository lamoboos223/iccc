namespace Iccc.Application.Assistant;

public sealed record AssistantState
{
    public Guid? ServiceId { get; init; }
    public bool ServiceFound { get; init; }
    public bool ArchitectureComplete { get; init; }
    public bool WorkloadDefined { get; init; }
    public bool WorkloadUsesRequestsPerSecond { get; init; }
    public bool HasExecution { get; init; }
    public bool MeasurementUsable { get; init; }
    public bool AuthorizeTests { get; init; }
}

public sealed record AssistantPlan
{
    public required IReadOnlyList<string> Tools { get; init; }
    public required IReadOnlyList<string> Clarifications { get; init; }
    public required bool MayExecuteTest { get; init; }
    public required bool MayCalculate { get; init; }
}

public static class AssistantPlanner
{
    public static readonly string[] ToolCatalog =
    [
        "get_service_details",
        "get_service_architecture",
        "get_dependency_details",
        "get_workload_profiles",
        "create_workload_profile",
        "create_load_test",
        "validate_load_test",
        "execute_load_test",
        "get_load_test_status",
        "get_load_test_results",
        "get_telemetry_metrics",
        "calculate_infrastructure_capacity",
        "get_infrastructure_catalog",
        "calculate_infrastructure_cost",
        "compare_configurations",
        "optimize_infrastructure_configuration",
        "generate_infrastructure_report"
    ];

    public static AssistantPlan Plan(AssistantState state, string message)
    {
        var tools = new List<string>();
        var questions = new List<string>();
        var text = message ?? "";
        var wantsEstimate = text.Contains("estimate", StringComparison.OrdinalIgnoreCase)
            || text.Contains("infrastructure", StringComparison.OrdinalIgnoreCase)
            || text.Contains("capacity", StringComparison.OrdinalIgnoreCase)
            || text.Contains("recommend", StringComparison.OrdinalIgnoreCase);

        if (state.ServiceId is null || !state.ServiceFound)
        {
            questions.Add("Which registered service should this apply to?");
            return new AssistantPlan { Tools = tools, Clarifications = questions, MayExecuteTest = false, MayCalculate = false };
        }

        tools.Add("get_service_details");
        tools.Add("get_service_architecture");
        tools.Add("get_dependency_details");
        tools.Add("get_workload_profiles");

        if (!state.ArchitectureComplete)
        {
            questions.Add("The architecture is incomplete. Add the runtime, deployment model, and dependencies before sizing.");
        }

        if (!state.WorkloadDefined || !state.WorkloadUsesRequestsPerSecond)
        {
            questions.Add("Define the workload in requests per second. Concurrent users are not used as the sizing metric.");
            tools.Add("create_workload_profile");
        }

        if (!state.HasExecution || !state.MeasurementUsable)
        {
            tools.Add("create_load_test");
            tools.Add("validate_load_test");
            if (state.AuthorizeTests)
            {
                tools.Add("execute_load_test");
            }
            else
            {
                questions.Add("No usable measurement is on file. I can prepare a test, and I will not run it until you authorize it.");
            }

            return new AssistantPlan
            {
                Tools = tools,
                Clarifications = questions,
                MayExecuteTest = state.AuthorizeTests && state.ArchitectureComplete && state.WorkloadUsesRequestsPerSecond,
                MayCalculate = false
            };
        }

        if (wantsEstimate || text.Contains("result", StringComparison.OrdinalIgnoreCase) || text.Length > 0)
        {
            tools.Add("get_load_test_results");
            tools.Add("get_telemetry_metrics");
            tools.Add("calculate_infrastructure_capacity");
            tools.Add("get_infrastructure_catalog");
            tools.Add("calculate_infrastructure_cost");
            tools.Add("compare_configurations");
            tools.Add("optimize_infrastructure_configuration");
            tools.Add("generate_infrastructure_report");
        }

        return new AssistantPlan
        {
            Tools = tools,
            Clarifications = questions,
            MayExecuteTest = false,
            MayCalculate = wantsEstimate || tools.Contains("calculate_infrastructure_capacity")
        };
    }
}
