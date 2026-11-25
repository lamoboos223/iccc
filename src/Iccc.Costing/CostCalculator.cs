namespace Iccc.Costing;

public static class CostCalculator
{
    public static CostEstimate Calculate(CostRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.HoursPerMonth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Hours per month must be positive.");
        }

        var lines = new List<CostLine>();
        decimal total = 0;
        var complete = true;

        foreach (var quantity in request.Quantities)
        {
            var entry = request.Catalog.Entries.FirstOrDefault(candidate =>
                candidate.Sku == quantity.Sku
                && candidate.Component == quantity.Component
                && IsCurrent(candidate, request.AsOf));

            if (entry is null)
            {
                complete = false;
                lines.Add(new CostLine
                {
                    Sku = quantity.Sku,
                    Component = quantity.Component,
                    MonthlyAmount = null,
                    Evidence = "Insufficient",
                    Limitation = "No catalog price is valid for this SKU on the requested date."
                });
                continue;
            }

            var monthly = Monthly(entry, quantity.Quantity, request.HoursPerMonth);
            total += monthly;
            lines.Add(new CostLine
            {
                Sku = quantity.Sku,
                Component = quantity.Component,
                MonthlyAmount = monthly,
                Evidence = "CatalogPrice",
                Limitation = null
            });
        }

        decimal? perThousand = null;
        decimal? perMillion = null;
        var note = "Average traffic was not supplied. Cost per request is omitted so peak capacity is not mistaken for monthly volume.";
        if (request.AverageRequestsPerSecond is decimal average && average > 0)
        {
            var requests = average * request.HoursPerMonth * 3600m;
            perThousand = total / (requests / 1000m);
            perMillion = total / (requests / 1_000_000m);
            note = "Cost per request uses the supplied average request rate. The rate is an input, not a measured monthly total.";
        }

        var instanceQuantity = request.Quantities
            .Where(quantity => quantity.Component == CostComponent.Compute)
            .Sum(quantity => quantity.Quantity);
        decimal? perInstance = instanceQuantity > 0 ? total / instanceQuantity : null;

        return new CostEstimate
        {
            CatalogVersion = request.Catalog.CatalogVersion,
            Currency = request.Catalog.Currency,
            Complete = complete,
            Lines = lines,
            MonthlyTotal = total,
            AnnualTotal = total * 12m,
            CostPerThousandRequests = perThousand,
            CostPerMillionRequests = perMillion,
            CostPerInstance = perInstance,
            TrafficBasisNote = note
        };
    }

    private static bool IsCurrent(PriceEntry entry, DateOnly asOf)
    {
        if (entry.ValidFrom is DateOnly from && asOf < from)
        {
            return false;
        }

        if (entry.ValidTo is DateOnly to && asOf > to)
        {
            return false;
        }

        return true;
    }

    private static decimal Monthly(PriceEntry entry, decimal quantity, int hoursPerMonth)
    {
        var amount = entry.UnitPrice * quantity;
        return entry.Unit switch
        {
            PriceUnit.PerMonth => amount,
            PriceUnit.PerHour => amount * hoursPerMonth,
            PriceUnit.PerGbMonth => amount,
            PriceUnit.Capital => amount / Math.Max(1, entry.DepreciationMonths),
            _ => throw new ArgumentOutOfRangeException(nameof(entry))
        };
    }
}
