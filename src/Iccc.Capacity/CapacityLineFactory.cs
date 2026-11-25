namespace Iccc.Capacity;

internal static class CapacityLineFactory
{
    public static CapacityLine Line(
        string component,
        string metric,
        decimal? value,
        string unit,
        EvidenceKind evidence,
        string formulaId,
        string formula,
        IReadOnlyList<FormulaInput> inputs,
        decimal ratio,
        string? limitation = null,
        bool blocksApproval = false)
    {
        return new CapacityLine
        {
            Component = component,
            Metric = metric,
            Value = value,
            Unit = unit,
            Evidence = evidence,
            FormulaId = formulaId,
            Formula = formula,
            Inputs = inputs,
            Confidence = ScalingMath.ConfidenceFor(evidence, ratio),
            Limitation = limitation,
            BlocksApproval = blocksApproval
        };
    }
}
