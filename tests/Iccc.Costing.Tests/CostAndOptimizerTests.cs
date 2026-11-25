using Iccc.Costing;

namespace Iccc.Costing.Tests;

public class CostAndOptimizerTests
{
    [Fact]
    public void Prices_come_from_the_catalog_and_unit_cost_needs_average_traffic()
    {
        var estimate = CostCalculator.Calculate(new CostRequest
        {
            AsOf = new DateOnly(2026, 11, 25),
            AverageRequestsPerSecond = 100,
            Catalog = Catalog(),
            Quantities =
            [
                new CostQuantity { Sku = "vm-4c8g", Component = CostComponent.Compute, Quantity = 4, UnitLabel = "instances" },
                new CostQuantity { Sku = "ssd", Component = CostComponent.Storage, Quantity = 100, UnitLabel = "GB" },
                new CostQuantity { Sku = "sql", Component = CostComponent.License, Quantity = 1, UnitLabel = "license" }
            ]
        });

        Assert.Equal(1310m, estimate.MonthlyTotal);
        Assert.Equal(15720m, estimate.AnnualTotal);
        Assert.Equal(4.9847792998m, decimal.Round(estimate.CostPerMillionRequests!.Value, 10));
        Assert.Equal("catalog-2026.11", estimate.CatalogVersion);
        Assert.True(estimate.Complete);
    }

    [Fact]
    public void Missing_or_expired_prices_are_not_replaced_with_a_guess()
    {
        var estimate = CostCalculator.Calculate(new CostRequest
        {
            AsOf = new DateOnly(2027, 1, 1),
            Catalog = Catalog(),
            Quantities =
            [
                new CostQuantity { Sku = "vm-4c8g", Component = CostComponent.Compute, Quantity = 1, UnitLabel = "instances" }
            ]
        });

        Assert.False(estimate.Complete);
        Assert.Equal(0, estimate.MonthlyTotal);
        Assert.Null(estimate.Lines[0].MonthlyAmount);
        Assert.Null(estimate.CostPerThousandRequests);
    }

    [Fact]
    public void On_premises_capital_is_depreciated_and_kept_apart_from_capacity()
    {
        var estimate = CostCalculator.Calculate(new CostRequest
        {
            AsOf = new DateOnly(2026, 11, 25),
            AverageRequestsPerSecond = 10,
            Catalog = new PriceCatalog
            {
                CatalogVersion = "onprem-1",
                Currency = "USD",
                Entries =
                [
                    new PriceEntry
                    {
                        Sku = "host",
                        Component = CostComponent.Compute,
                        UnitPrice = 3600,
                        Unit = PriceUnit.Capital,
                        DepreciationMonths = 36
                    }
                ]
            },
            Quantities =
            [
                new CostQuantity { Sku = "host", Component = CostComponent.Compute, Quantity = 2, UnitLabel = "hosts" }
            ]
        });

        Assert.Equal(200m, estimate.MonthlyTotal);
    }

    [Fact]
    public void Cheapest_measured_configuration_is_rejected_when_it_misses_the_latency_sla()
    {
        var result = ConfigurationOptimizer.Optimize(new OptimizationRequest
        {
            TargetRps = 10000,
            MaxP95LatencyMs = 200,
            MeasuredRpsPerInstance = 1250,
            MeasuredP95Ms = 90,
            MeasuredSku = "vm-4c8g",
            MeasuredVcpu = 4,
            MeasuredMemoryGb = 8,
            TestedInstanceCount = 8,
            Candidates =
            [
                new ScenarioCandidate
                {
                    Name = "cheap-measured",
                    Sku = "vm-2c4g",
                    InstanceCount = 4,
                    MonthlyCostPerInstance = 100,
                    HasDirectMeasurement = true,
                    MeasuredAggregateRps = 10000,
                    MeasuredP95Ms = 400,
                    MeasuredErrorRate = 0.001m,
                    Vcpu = 2,
                    MemoryGb = 4
                },
                new ScenarioCandidate
                {
                    Name = "compliant-measured",
                    Sku = "vm-4c8g",
                    InstanceCount = 8,
                    MonthlyCostPerInstance = 100,
                    HasDirectMeasurement = true,
                    MeasuredAggregateRps = 12000,
                    MeasuredP95Ms = 90,
                    MeasuredErrorRate = 0.001m,
                    Vcpu = 4,
                    MemoryGb = 8
                },
                new ScenarioCandidate
                {
                    Name = "cheaper-untested",
                    Sku = "vm-2c4g",
                    InstanceCount = 2,
                    MonthlyCostPerInstance = 50,
                    Vcpu = 2,
                    MemoryGb = 4
                }
            ]
        });

        Assert.Equal("compliant-measured", result.Selected!.Name);
        Assert.Equal(800, result.Selected.MonthlyCost);
        Assert.Contains(result.Evaluations, item => item.Name == "cheap-measured" && !item.MeetsSla);
        Assert.Contains(result.Evaluations, item => item.Name == "cheaper-untested" && !item.MeetsSla && item.LatencyEvidence == "Insufficient");
    }

    private static PriceCatalog Catalog() => new()
    {
        CatalogVersion = "catalog-2026.11",
        Currency = "USD",
        Entries =
        [
            new PriceEntry
            {
                Sku = "vm-4c8g",
                Component = CostComponent.Compute,
                UnitPrice = 200,
                Unit = PriceUnit.PerMonth,
                ValidFrom = new DateOnly(2026, 1, 1),
                ValidTo = new DateOnly(2026, 12, 31)
            },
            new PriceEntry
            {
                Sku = "ssd",
                Component = CostComponent.Storage,
                UnitPrice = 0.10m,
                Unit = PriceUnit.PerGbMonth
            },
            new PriceEntry
            {
                Sku = "sql",
                Component = CostComponent.License,
                UnitPrice = 500,
                Unit = PriceUnit.PerMonth
            }
        ]
    };
}
