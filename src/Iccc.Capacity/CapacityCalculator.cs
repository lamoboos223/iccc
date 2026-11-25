namespace Iccc.Capacity;

public static class CapacityCalculator
{
    public const string FormulaVersion = "2026.1";

    public static CapacityCalculationResult Calculate(CapacityCalculationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var lines = new List<CapacityLine>();
        var bottlenecks = new List<BottleneckFinding>();
        var limitations = new List<string>();
        var assumptions = new List<string>();

        if (input.Policy.HeadroomFactor < 1
            || input.Policy.MaxCpuUtilization <= 0
            || input.Policy.MaxCpuUtilization > 1
            || input.Policy.MaxMemoryUtilization <= 0
            || input.Policy.MaxMemoryUtilization > 1
            || input.Policy.HardExtrapolationRatio < input.Policy.MaxExtrapolationRatio
            || input.Policy.HorizontalScalingOverheadPerDoubling < 0)
        {
            limitations.Add("Capacity policy values are outside the supported range.");
            return Finish(CalculationStatus.InvalidMeasurement, false, lines, bottlenecks, limitations, assumptions);
        }

        if (input.Workload.TargetValue <= 0)
        {
            limitations.Add("The workload target must be positive.");
            return Finish(CalculationStatus.InvalidMeasurement, false, lines, bottlenecks, limitations, assumptions);
        }

        if (input.Observation.GeneratorSaturated)
        {
            lines.Add(CapacityLineFactory.Line(
                "load-generator", "measurement-validity", null, "flag", EvidenceKind.Insufficient,
                "GENERATOR-GATE", "a saturated generator invalidates service capacity",
                [new FormulaInput("generatorSaturated", 1, "flag", EvidenceKind.Measured)],
                1,
                "The load generator was saturated. Throughput and latency cannot be attributed to the service.",
                blocksApproval: true));
            limitations.Add("The load generator was saturated, so this run is not a service measurement.");
            return Finish(CalculationStatus.InvalidMeasurement, false, lines, bottlenecks, limitations, assumptions);
        }

        if (input.Observation.AchievedRps <= 0)
        {
            limitations.Add("Measured throughput must be greater than zero.");
            return Finish(CalculationStatus.InsufficientEvidence, false, lines, bottlenecks, limitations, assumptions);
        }

        if (input.Observation.SustainDuration < TimeSpan.FromSeconds(input.Policy.MinSustainSeconds))
        {
            limitations.Add(
                $"Sustained load lasted {input.Observation.SustainDuration.TotalSeconds:0}s, below the {input.Policy.MinSustainSeconds}s minimum.");
            return Finish(CalculationStatus.InsufficientEvidence, false, lines, bottlenecks, limitations, assumptions);
        }

        if (input.Workload.Metric != WorkloadMetric.RequestsPerSecond)
        {
            limitations.Add(
                "Instance sizing requires a requests-per-second target. Concurrent users, requests per minute, and requests per hour are not converted into infrastructure counts.");
            lines.Add(CapacityLineFactory.Line(
                "application", "serving-instances", null, "instances", EvidenceKind.Insufficient,
                "APP-METRIC-GATE", "only a requests-per-second target can size instances",
                [], 1, "The workload metric was not requests per second.", blocksApproval: true));
            return Finish(CalculationStatus.InsufficientEvidence, false, lines, bottlenecks, limitations, assumptions);
        }

        assumptions.Add($"Availability target {input.Sla.AvailabilityTarget:P2} is a requirement, not a measured availability result.");

        var sla = SlaEvaluator.Evaluate(input);
        lines.AddRange(sla.Lines);
        limitations.AddRange(sla.Limitations);
        if (!sla.CanSize)
        {
            return Finish(CalculationStatus.InsufficientEvidence, false, lines, bottlenecks, limitations, assumptions);
        }

        var blockers = new List<string>();
        DependencyCapacity.CollectBlockers(input, blockers, bottlenecks);
        ApplicationCapacity.Contribute(input, sla.SustainableRps, blockers, lines, limitations, assumptions);
        DependencyCapacity.Contribute(input, sla.SustainableRps, lines, limitations, assumptions);

        var blocking = lines.Where(line => line.BlocksApproval && (line.Value is null || line.Evidence == EvidenceKind.Insufficient)).ToList();
        var policyBreach = bottlenecks.Any(item => item.Severity is BottleneckSeverity.PolicyBreach or BottleneckSeverity.BlocksGrowth);
        var serving = lines.FirstOrDefault(line => line.Component == "application" && line.Metric == "serving-instances");
        var servingValidated = serving?.Value is not null
            && serving.Evidence is EvidenceKind.Measured or EvidenceKind.Calculated
            && !policyBreach
            && blocking.Count == 0;

        CalculationStatus status;
        if (blocking.Count > 0 && serving?.Value is null)
        {
            status = CalculationStatus.InsufficientEvidence;
        }
        else if (blocking.Count > 0 || lines.Any(line => line.Evidence == EvidenceKind.Insufficient) || policyBreach)
        {
            status = CalculationStatus.Partial;
        }
        else
        {
            status = CalculationStatus.Sufficient;
        }

        return Finish(status, servingValidated, lines, bottlenecks, limitations, assumptions);
    }

    private static CapacityCalculationResult Finish(
        CalculationStatus status,
        bool approvable,
        List<CapacityLine> lines,
        List<BottleneckFinding> bottlenecks,
        List<string> limitations,
        List<string> assumptions)
    {
        return new CapacityCalculationResult
        {
            FormulaVersion = FormulaVersion,
            Status = status,
            ApprovableAsValidated = approvable && status is CalculationStatus.Sufficient or CalculationStatus.Partial,
            Lines = lines,
            Bottlenecks = bottlenecks,
            Limitations = limitations.Distinct().ToList(),
            Assumptions = assumptions.Distinct().ToList()
        };
    }
}
