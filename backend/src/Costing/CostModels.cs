namespace Iccc.Costing;

public enum PriceUnit
{
    PerMonth,
    PerHour,
    PerGbMonth,
    Capital
}

public enum CostComponent
{
    Compute,
    Storage,
    License,
    Support,
    Network,
    Backup,
    Observability,
    Power,
    Overhead
}

public sealed record PriceEntry
{
    public required string Sku { get; init; }
    public required CostComponent Component { get; init; }
    public required decimal UnitPrice { get; init; }
    public required PriceUnit Unit { get; init; }
    public decimal Quantity { get; init; } = 1;
    public int DepreciationMonths { get; init; } = 36;
    public DateOnly? ValidFrom { get; init; }
    public DateOnly? ValidTo { get; init; }
}

public sealed record PriceCatalog
{
    public required string CatalogVersion { get; init; }
    public required string Currency { get; init; }
    public required IReadOnlyList<PriceEntry> Entries { get; init; }
}

public sealed record CostQuantity
{
    public required string Sku { get; init; }
    public required CostComponent Component { get; init; }
    public required decimal Quantity { get; init; }
    public required string UnitLabel { get; init; }
}

public sealed record CostRequest
{
    public required PriceCatalog Catalog { get; init; }
    public required IReadOnlyList<CostQuantity> Quantities { get; init; }
    public required DateOnly AsOf { get; init; }
    public decimal? AverageRequestsPerSecond { get; init; }
    public int HoursPerMonth { get; init; } = 730;
}

public sealed record CostLine
{
    public required string Sku { get; init; }
    public required CostComponent Component { get; init; }
    public decimal? MonthlyAmount { get; init; }
    public required string Evidence { get; init; }
    public string? Limitation { get; init; }
}

public sealed record CostEstimate
{
    public required string CatalogVersion { get; init; }
    public required string Currency { get; init; }
    public required bool Complete { get; init; }
    public required IReadOnlyList<CostLine> Lines { get; init; }
    public required decimal MonthlyTotal { get; init; }
    public required decimal AnnualTotal { get; init; }
    public decimal? CostPerThousandRequests { get; init; }
    public decimal? CostPerMillionRequests { get; init; }
    public decimal? CostPerInstance { get; init; }
    public required string TrafficBasisNote { get; init; }
}
