namespace Iccc.Capacity;

public static class ScalingMath
{
    public static int CeilToInt(decimal value)
    {
        if (value <= 0)
        {
            return 0;
        }

        var truncated = decimal.Truncate(value);
        if (truncated != value)
        {
            truncated += 1;
        }

        return (int)truncated;
    }

    public static decimal DoublingsBeyond(decimal requiredUnits, decimal testedUnits)
    {
        if (requiredUnits <= testedUnits || testedUnits <= 0)
        {
            return 0;
        }

        return (decimal)Math.Log2((double)(requiredUnits / testedUnits));
    }

    public static decimal OverheadMultiplier(decimal requiredUnits, decimal testedUnits, decimal overheadPerDoubling)
    {
        var doublings = DoublingsBeyond(requiredUnits, testedUnits);
        if (doublings == 0 || overheadPerDoubling == 0)
        {
            return 1;
        }

        return (decimal)Math.Pow((double)(1 + overheadPerDoubling), (double)doublings);
    }

    public static decimal Derate(decimal? utilization, decimal cap)
    {
        if (utilization is not decimal observed || observed <= 0 || observed <= cap)
        {
            return 1;
        }

        return cap / observed;
    }

    public static decimal ConfidenceFor(EvidenceKind kind, decimal extrapolationRatio)
    {
        return kind switch
        {
            EvidenceKind.Measured => 0.95m,
            EvidenceKind.Calculated => extrapolationRatio <= 1m ? 0.90m : 0.75m,
            EvidenceKind.Extrapolated => extrapolationRatio <= 2m ? 0.55m : 0.35m,
            EvidenceKind.Assumption => 0.40m,
            _ => 0m
        };
    }
}
