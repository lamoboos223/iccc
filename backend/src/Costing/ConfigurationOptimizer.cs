using Iccc.Capacity;

namespace Iccc.Costing;

public sealed record ScenarioCandidate
{
    public required string Name { get; init; }
    public required string Sku { get; init; }
    public required int InstanceCount { get; init; }
    public required decimal MonthlyCostPerInstance { get; init; }
    public bool HasDirectMeasurement { get; init; }
    public decimal? MeasuredAggregateRps { get; init; }
    public decimal? MeasuredP95Ms { get; init; }
    public decimal? MeasuredErrorRate { get; init; }
    public decimal Vcpu { get; init; }
    public decimal MemoryGb { get; init; }
}

public sealed record OptimizationRequest
{
    public required decimal TargetRps { get; init; }
    public decimal? MaxP95LatencyMs { get; init; }
    public decimal MaxErrorRate { get; init; } = 0.01m;
    public required decimal MeasuredRpsPerInstance { get; init; }
    public decimal? MeasuredP95Ms { get; init; }
    public required string MeasuredSku { get; init; }
    public required decimal MeasuredVcpu { get; init; }
    public required decimal MeasuredMemoryGb { get; init; }
    public required int TestedInstanceCount { get; init; }
    public required IReadOnlyList<ScenarioCandidate> Candidates { get; init; }
    public CapacityPolicy Policy { get; init; } = new();
}

public sealed record ScenarioEvaluation
{
    public required string Name { get; init; }
    public required string Sku { get; init; }
    public required int InstanceCount { get; init; }
    public decimal? ExpectedRps { get; init; }
    public decimal? LatencyMs { get; init; }
    public required string ThroughputEvidence { get; init; }
    public required string LatencyEvidence { get; init; }
    public required bool MeetsSla { get; init; }
    public required decimal MonthlyCost { get; init; }
    public decimal? CostPerThousandRequests { get; init; }
    public required string Reason { get; init; }
}

public sealed record OptimizationResult
{
    public ScenarioEvaluation? Selected { get; init; }
    public required IReadOnlyList<ScenarioEvaluation> Evaluations { get; init; }
    public required string Explanation { get; init; }
}

public static class ConfigurationOptimizer
{
    public static OptimizationResult Optimize(OptimizationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var evaluations = request.Candidates.Select(candidate => Evaluate(request, candidate)).ToList();
        var selected = evaluations
            .Where(evaluation => evaluation.MeetsSla)
            .OrderBy(evaluation => evaluation.MonthlyCost)
            .ThenBy(evaluation => evaluation.InstanceCount)
            .FirstOrDefault();

        var explanation = selected is null
            ? "No candidate satisfies the SLA with evidence the optimizer is allowed to use. Untested SKUs are not treated as SLA-compliant."
            : $"Selected {selected.Name} at {selected.MonthlyCost} per month. Throughput evidence is {selected.ThroughputEvidence}; latency evidence is {selected.LatencyEvidence}.";

        return new OptimizationResult
        {
            Selected = selected,
            Evaluations = evaluations,
            Explanation = explanation
        };
    }

    private static ScenarioEvaluation Evaluate(OptimizationRequest request, ScenarioCandidate candidate)
    {
        var cost = candidate.MonthlyCostPerInstance * candidate.InstanceCount;
        decimal? costPerThousand = request.TargetRps <= 0
            ? null
            : cost / (request.TargetRps * 730m * 3600m / 1000m);

        if (candidate.HasDirectMeasurement)
        {
            var throughput = candidate.MeasuredAggregateRps;
            var latency = candidate.MeasuredP95Ms;
            var error = candidate.MeasuredErrorRate ?? 0;
            var throughputOk = throughput is decimal rps && rps >= request.TargetRps;
            var latencyOk = request.MaxP95LatencyMs is null || (latency is decimal p95 && p95 <= request.MaxP95LatencyMs);
            var errorOk = error <= request.MaxErrorRate;
            var meets = throughputOk && latencyOk && errorOk;
            return new ScenarioEvaluation
            {
                Name = candidate.Name,
                Sku = candidate.Sku,
                InstanceCount = candidate.InstanceCount,
                ExpectedRps = throughput,
                LatencyMs = latency,
                ThroughputEvidence = "Measured",
                LatencyEvidence = latency is null ? "Insufficient" : "Measured",
                MeetsSla = meets,
                MonthlyCost = cost,
                CostPerThousandRequests = costPerThousand,
                Reason = meets
                    ? "This configuration was measured and met the SLA."
                    : "This configuration was measured and did not meet the SLA."
            };
        }

        if (candidate.Sku != request.MeasuredSku
            || candidate.Vcpu != request.MeasuredVcpu
            || candidate.MemoryGb != request.MeasuredMemoryGb)
        {
            var cpuRatio = request.MeasuredVcpu <= 0 ? 0 : candidate.Vcpu / request.MeasuredVcpu;
            var memoryRatio = request.MeasuredMemoryGb <= 0 ? 0 : candidate.MemoryGb / request.MeasuredMemoryGb;
            var scale = Math.Min(cpuRatio, memoryRatio) * request.Policy.UnmeasuredSkuDerate;
            var estimated = request.MeasuredRpsPerInstance * scale * candidate.InstanceCount;
            return new ScenarioEvaluation
            {
                Name = candidate.Name,
                Sku = candidate.Sku,
                InstanceCount = candidate.InstanceCount,
                ExpectedRps = estimated,
                LatencyMs = null,
                ThroughputEvidence = "Extrapolated",
                LatencyEvidence = "Insufficient",
                MeetsSla = false,
                MonthlyCost = cost,
                CostPerThousandRequests = costPerThousand,
                Reason = "A different instance size was not load-tested. Latency SLA compliance is not claimed, even when the derated throughput estimate is high enough."
            };
        }

        var perInstanceLoad = candidate.InstanceCount <= 0 ? decimal.MaxValue : request.TargetRps / candidate.InstanceCount;
        var withinMeasuredPoint = perInstanceLoad <= request.MeasuredRpsPerInstance;
        var overhead = ScalingMath.OverheadMultiplier(candidate.InstanceCount, request.TestedInstanceCount, request.Policy.HorizontalScalingOverheadPerDoubling);
        var expected = request.MeasuredRpsPerInstance * candidate.InstanceCount / overhead;

        return new ScenarioEvaluation
        {
            Name = candidate.Name,
            Sku = candidate.Sku,
            InstanceCount = candidate.InstanceCount,
            ExpectedRps = expected,
            LatencyMs = withinMeasuredPoint ? request.MeasuredP95Ms : null,
            ThroughputEvidence = candidate.InstanceCount == request.TestedInstanceCount ? "Calculated" : "Extrapolated",
            LatencyEvidence = withinMeasuredPoint ? "Assumption" : "Insufficient",
            MeetsSla = false,
            MonthlyCost = cost,
            CostPerThousandRequests = costPerThousand,
            Reason = withinMeasuredPoint
                ? "Per-instance load does not exceed the tested operating point. Latency is an assumption, so this row is not selected as SLA-compliant."
                : "This count would push more traffic through each instance than the test measured. Latency is unknown, so the SLA is not claimed."
        };
    }
}
