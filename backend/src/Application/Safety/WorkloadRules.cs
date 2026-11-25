using Iccc.Domain;

namespace Iccc.Application.Safety;

public sealed record WorkloadDraft
{
    public required WorkloadMetricKind Metric { get; init; }
    public required decimal TargetValue { get; init; }
    public int? VirtualUsers { get; init; }
    public decimal? TargetRequestsPerSecond { get; init; }
    public int RampUpSeconds { get; init; }
    public int SustainSeconds { get; init; }
    public int RampDownSeconds { get; init; }
    public decimal ReadRatio { get; init; } = 1;
    public IReadOnlyList<decimal> EndpointWeights { get; init; } = [];
}

public static class WorkloadRules
{
    public static IReadOnlyList<string> Validate(WorkloadDraft draft)
    {
        var errors = new List<string>();
        if (draft.TargetValue <= 0)
        {
            errors.Add("The target value must be positive.");
        }

        if (draft.Metric == WorkloadMetricKind.ConcurrentUsers && draft.TargetRequestsPerSecond is null or <= 0)
        {
            errors.Add("Concurrent users are not requests per second. Add a requests-per-second target before this workload can size infrastructure.");
        }

        if (draft.RampUpSeconds < 0 || draft.SustainSeconds <= 0 || draft.RampDownSeconds < 0)
        {
            errors.Add("Sustain duration must be positive, and ramp periods cannot be negative.");
        }

        if (draft.ReadRatio < 0 || draft.ReadRatio > 1)
        {
            errors.Add("The read ratio must be between 0 and 1.");
        }

        if (draft.EndpointWeights.Count > 0 && Math.Abs(draft.EndpointWeights.Sum() - 100m) > 0.01m)
        {
            errors.Add("Endpoint traffic weights must sum to 100.");
        }

        if (draft.VirtualUsers is int users && users <= 0)
        {
            errors.Add("Virtual users, when set, must be positive and are stored separately from the request rate.");
        }

        return errors;
    }
}
