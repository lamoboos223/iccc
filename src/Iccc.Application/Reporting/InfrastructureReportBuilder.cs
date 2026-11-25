using System.Globalization;
using System.Net;
using System.Text;
using Iccc.Capacity;
using Iccc.Costing;

namespace Iccc.Application.Reporting;

public sealed record ReportDocument
{
    public required string ServiceName { get; init; }
    public required string ArchitectureSummary { get; init; }
    public required string WorkloadSummary { get; init; }
    public required string TestSummary { get; init; }
    public required bool DemonstrationFixture { get; init; }
    public required CapacityCalculationResult Calculation { get; init; }
    public CostEstimate? Cost { get; init; }
    public OptimizationResult? Comparison { get; init; }
    public required IReadOnlyList<string> ApprovalHistory { get; init; }
}

public static class InfrastructureReportBuilder
{
    public static string ToMarkdown(ReportDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# Infrastructure recommendation: {document.ServiceName}");
        builder.AppendLine();
        if (document.DemonstrationFixture)
        {
            builder.AppendLine("> Demonstration fixture. These figures were loaded to exercise the platform. They are not a live measurement of a deployed service.");
            builder.AppendLine();
        }

        builder.AppendLine($"Formula version: {document.Calculation.FormulaVersion}");
        builder.AppendLine($"Calculation status: {document.Calculation.Status}");
        builder.AppendLine($"Approvable as validated: {document.Calculation.ApprovableAsValidated}");
        if (document.Cost is not null)
        {
            builder.AppendLine($"Catalog version: {document.Cost.CatalogVersion}");
        }

        builder.AppendLine();
        builder.AppendLine("## Service and architecture");
        builder.AppendLine(document.ArchitectureSummary);
        builder.AppendLine();
        builder.AppendLine("## Workload");
        builder.AppendLine(document.WorkloadSummary);
        builder.AppendLine();
        builder.AppendLine("## Test");
        builder.AppendLine(document.TestSummary);
        builder.AppendLine();
        builder.AppendLine("## Recommendation");
        builder.AppendLine("| Component | Metric | Value | Unit | Evidence | Confidence | Formula | Limitation |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var line in document.Calculation.Lines)
        {
            builder.Append("| ").Append(line.Component)
                .Append(" | ").Append(line.Metric)
                .Append(" | ").Append(line.Value?.ToString(CultureInfo.InvariantCulture) ?? "")
                .Append(" | ").Append(line.Unit)
                .Append(" | ").Append(line.Evidence)
                .Append(" | ").Append(line.Confidence.ToString("0.00", CultureInfo.InvariantCulture))
                .Append(" | ").Append(line.FormulaId)
                .Append(" | ").Append(line.Limitation?.Replace("|", "/", StringComparison.Ordinal) ?? "")
                .AppendLine(" |");
        }

        builder.AppendLine();
        builder.AppendLine("## Bottlenecks");
        if (document.Calculation.Bottlenecks.Count == 0)
        {
            builder.AppendLine("None recorded.");
        }

        foreach (var bottleneck in document.Calculation.Bottlenecks)
        {
            builder.AppendLine($"- {bottleneck.Code} ({bottleneck.Severity}): {bottleneck.Explanation}");
        }

        builder.AppendLine();
        builder.AppendLine("## Assumptions");
        foreach (var assumption in document.Calculation.Assumptions)
        {
            builder.AppendLine($"- {assumption}");
        }

        builder.AppendLine();
        builder.AppendLine("## Limitations");
        foreach (var limitation in document.Calculation.Limitations)
        {
            builder.AppendLine($"- {limitation}");
        }

        if (document.Cost is not null)
        {
            builder.AppendLine();
            builder.AppendLine("## Cost");
            builder.AppendLine($"Monthly: {document.Cost.MonthlyTotal.ToString(CultureInfo.InvariantCulture)} {document.Cost.Currency}");
            builder.AppendLine($"Annual: {document.Cost.AnnualTotal.ToString(CultureInfo.InvariantCulture)} {document.Cost.Currency}");
            builder.AppendLine(document.Cost.TrafficBasisNote);
            builder.AppendLine("Capacity quantities and catalog prices are separate inputs. A price is never treated as a measurement.");
        }

        if (document.Comparison is not null)
        {
            builder.AppendLine();
            builder.AppendLine("## Scenarios");
            builder.AppendLine(document.Comparison.Explanation);
            foreach (var evaluation in document.Comparison.Evaluations)
            {
                builder.AppendLine($"- {evaluation.Name}: cost {evaluation.MonthlyCost.ToString(CultureInfo.InvariantCulture)}, SLA claimed {evaluation.MeetsSla}, throughput {evaluation.ThroughputEvidence}, latency {evaluation.LatencyEvidence}. {evaluation.Reason}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Approval history");
        foreach (var entry in document.ApprovalHistory)
        {
            builder.AppendLine($"- {entry}");
        }

        return builder.ToString();
    }

    public static string ToCsv(ReportDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("component,metric,value,unit,evidence,confidence,formula,limitation");
        foreach (var line in document.Calculation.Lines)
        {
            builder.Append(Csv(line.Component)).Append(',')
                .Append(Csv(line.Metric)).Append(',')
                .Append(line.Value?.ToString(CultureInfo.InvariantCulture) ?? "").Append(',')
                .Append(Csv(line.Unit)).Append(',')
                .Append(line.Evidence).Append(',')
                .Append(line.Confidence.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(Csv(line.FormulaId)).Append(',')
                .Append(Csv(line.Limitation ?? ""))
                .AppendLine();
        }

        return builder.ToString();
    }

    public static string ToHtml(ReportDocument document)
    {
        var markdown = ToMarkdown(document);
        return "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Infrastructure recommendation</title></head><body><pre>"
            + WebUtility.HtmlEncode(markdown)
            + "</pre></body></html>";
    }

    private static string Csv(string value)
    {
        var escaped = value.Replace("\"", "\"\"", StringComparison.Ordinal);
        return $"\"{escaped}\"";
    }
}
