namespace Iccc.Capacity;

internal sealed record SlaEvaluation(
    bool CanSize,
    decimal SustainableRps,
    bool SlaMetAtAchievedRate,
    IReadOnlyList<CapacityLine> Lines,
    IReadOnlyList<string> Limitations);

internal static class SlaEvaluator
{
    public static SlaEvaluation Evaluate(CapacityCalculationInput input)
    {
        var lines = new List<CapacityLine>();
        var limitations = new List<string>();
        var observation = input.Observation;
        var sla = input.Sla;
        var failures = new List<string>();

        if (sla.MaxErrorRate < 0 || sla.MaxErrorRate > 1)
        {
            limitations.Add("The maximum error rate must be between 0 and 1.");
            return new SlaEvaluation(false, 0, false, lines, limitations);
        }

        if (observation.ErrorRate > sla.MaxErrorRate)
        {
            failures.Add($"error rate {observation.ErrorRate:P2} exceeded {sla.MaxErrorRate:P2}");
        }

        if (!LatencyHolds(sla.MaxAverageLatencyMs, observation.AverageLatencyMs, "average latency", failures, limitations))
        {
            return new SlaEvaluation(false, 0, false, lines, limitations);
        }

        if (!LatencyHolds(sla.MaxP95LatencyMs, observation.P95LatencyMs, "p95 latency", failures, limitations))
        {
            return new SlaEvaluation(false, 0, false, lines, limitations);
        }

        if (!LatencyHolds(sla.MaxP99LatencyMs, observation.P99LatencyMs, "p99 latency", failures, limitations))
        {
            return new SlaEvaluation(false, 0, false, lines, limitations);
        }

        var met = failures.Count == 0;
        lines.Add(CapacityLineFactory.Line(
            "service",
            "sla-met-at-achieved-rps",
            met ? 1 : 0,
            "flag",
            EvidenceKind.Measured,
            "SLA-GATE",
            "achieved rate meets every supplied latency and error target",
            [
                new FormulaInput("achievedRps", observation.AchievedRps, "rps", EvidenceKind.Measured),
                new FormulaInput("errorRate", observation.ErrorRate, "ratio", EvidenceKind.Measured)
            ],
            1,
            met ? null : string.Join("; ", failures),
            blocksApproval: true));

        if (met)
        {
            return new SlaEvaluation(true, observation.AchievedRps, true, lines, limitations);
        }

        if (observation.SlaCompliantRps is decimal compliant
            && compliant > 0
            && compliant < observation.AchievedRps)
        {
            limitations.Add(
                $"The achieved rate of {observation.AchievedRps} rps violated the SLA ({string.Join("; ", failures)}). " +
                $"Sizing uses the measured SLA-compliant rate of {compliant} rps.");
            lines.Add(CapacityLineFactory.Line(
                "service",
                "sla-compliant-rps",
                compliant,
                "rps",
                EvidenceKind.Measured,
                "SLA-COMPLIANT-RATE",
                "highest measured rate that satisfied the SLA",
                [new FormulaInput("slaCompliantRps", compliant, "rps", EvidenceKind.Measured)],
                1,
                blocksApproval: true));
            return new SlaEvaluation(true, compliant, false, lines, limitations);
        }

        limitations.Add(
            $"The test reached {observation.AchievedRps} rps but violated the SLA ({string.Join("; ", failures)}). " +
            "No lower SLA-compliant operating point was recorded, so instance count is not estimated.");
        return new SlaEvaluation(false, 0, false, lines, limitations);
    }

    private static bool LatencyHolds(
        decimal? limit,
        decimal? observed,
        string name,
        List<string> failures,
        List<string> limitations)
    {
        if (limit is null)
        {
            return true;
        }

        if (observed is null)
        {
            limitations.Add($"The SLA requires {name}, but the test did not record it.");
            return false;
        }

        if (observed > limit)
        {
            failures.Add($"{name} {observed} ms exceeded {limit} ms");
        }

        return true;
    }
}
