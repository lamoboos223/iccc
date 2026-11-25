using System.Globalization;
using Iccc.Capacity;

namespace Iccc.Application.Assistant;

public static class RecommendationNarrator
{
    public static string Explain(CapacityCalculationResult result)
    {
        var parts = new List<string>
        {
            $"Calculation {result.FormulaVersion} finished with status {result.Status}.",
            result.ApprovableAsValidated
                ? "The serving capacity is inside the measured operating point and can be reviewed as validated, subject to the listed assumptions."
                : "This result is not a validated capacity certification."
        };

        foreach (var line in result.Lines.Where(line => line.Metric is "serving-instances" or "total-instances" or "serving-vcpu" or "serving-memory-gb" or "memory-mb" or "vcpu" or "workers"))
        {
            var value = line.Value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "not estimated";
            parts.Add($"{line.Component} {line.Metric} is {value} {line.Unit}. Evidence: {line.Evidence}. Formula: {line.FormulaId}.");
        }

        parts.AddRange(result.Limitations);
        parts.AddRange(result.Bottlenecks.Select(item => item.Explanation));
        return string.Join(" ", parts);
    }

    public static IReadOnlyList<decimal> Numbers(CapacityCalculationResult result)
    {
        return result.Lines
            .Where(line => line.Value is not null)
            .Select(line => line.Value!.Value)
            .Concat(result.Lines.Select(line => line.Confidence))
            .ToArray();
    }
}
