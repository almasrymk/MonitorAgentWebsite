using System.Globalization;
using System.Text;
using FluentValidation;
using MonitorAgent.BuildingBlocks.Application;
using MonitorAgent.Modules.Commerce.Domain;
using MonitorAgent.Modules.Customers.Contracts;
using MonitorAgent.SharedKernel;

namespace MonitorAgent.Modules.Commerce.Application;

/// <summary>Totals for one currency. Net is payments minus refunds; amounts in different currencies are never added together.</summary>
public sealed record CurrencyTotalDto(string Currency, decimal Payments, decimal Refunds, decimal Net, int PaymentCount, int RefundCount);
public sealed record MonthTotalDto(string Month, string Currency, decimal Net, int PaymentCount);
public sealed record PlanTotalDto(string ProductName, string PlanName, string Currency, decimal Net, int PaymentCount);
public sealed record ProviderTotalDto(string Provider, string Currency, decimal Net, int PaymentCount);
public sealed record RevenueReportDto(DateOnly From, DateOnly To, IReadOnlyList<CurrencyTotalDto> Totals, IReadOnlyList<MonthTotalDto> ByMonth,
    IReadOnlyList<PlanTotalDto> ByPlan, IReadOnlyList<ProviderTotalDto> ByProvider, bool Truncated);
public sealed record CsvFile(string FileName, string Content);

/// <summary>Money received and refunded between two days (UTC, both included), from the ledger.</summary>
public sealed record RevenueReportQuery(DateOnly From, DateOnly To) : IQuery<Result<RevenueReportDto>>;

/// <summary>The same ledger lines as a spreadsheet file, one row per payment or refund.</summary>
public sealed record LedgerExportQuery(DateOnly From, DateOnly To) : IQuery<Result<CsvFile>>;

internal sealed class RangeValidator : AbstractValidator<RevenueReportQuery>
{
    public RangeValidator() => RuleFor(x => x).Must(x => Reports.ValidRange(x.From, x.To)).WithName("to").WithMessage(Reports.RangeMessage);
}

internal sealed class ExportRangeValidator : AbstractValidator<LedgerExportQuery>
{
    public ExportRangeValidator() => RuleFor(x => x).Must(x => Reports.ValidRange(x.From, x.To)).WithName("to").WithMessage(Reports.RangeMessage);
}

internal static class Reports
{
    public const int MaxDays = 3 * 366;
    public const int MaxLines = 100_000;
    public const string RangeMessage = "Pick an end date on or after the start date, at most three years later.";

    public static bool ValidRange(DateOnly from, DateOnly to) => to >= from && to.DayNumber - from.DayNumber <= MaxDays;

    public static (DateTimeOffset From, DateTimeOffset Until) Bounds(DateOnly from, DateOnly to) =>
        (new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

    public static decimal Signed(LedgerEntry e) => e.Kind == LedgerEntryKind.Refund ? -e.Amount : e.Amount;
}

internal sealed class RevenueReportHandler(ICommerceStore store) : IQueryHandler<RevenueReportQuery, Result<RevenueReportDto>>
{
    public async Task<Result<RevenueReportDto>> HandleAsync(RevenueReportQuery query, CancellationToken ct)
    {
        var (from, until) = Reports.Bounds(query.From, query.To);
        var lines = await store.LedgerAsync(from, until, Reports.MaxLines + 1, ct);
        var truncated = lines.Count > Reports.MaxLines;
        if (truncated) lines = lines.Take(Reports.MaxLines).ToList();

        static int Paid(IEnumerable<LedgerLine> g) => g.Count(l => l.Entry.Kind == LedgerEntryKind.Payment);
        var totals = lines.GroupBy(l => l.Entry.Currency).OrderBy(g => g.Key).Select(g =>
        {
            var payments = g.Where(l => l.Entry.Kind == LedgerEntryKind.Payment).Sum(l => l.Entry.Amount);
            var refunds = g.Where(l => l.Entry.Kind == LedgerEntryKind.Refund).Sum(l => l.Entry.Amount);
            return new CurrencyTotalDto(g.Key, payments, refunds, payments - refunds, Paid(g), g.Count() - Paid(g));
        }).ToList();
        var byMonth = lines.GroupBy(l => (Month: l.Entry.At.UtcDateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture), l.Entry.Currency))
            .OrderBy(g => g.Key.Month).ThenBy(g => g.Key.Currency)
            .Select(g => new MonthTotalDto(g.Key.Month, g.Key.Currency, g.Sum(l => Reports.Signed(l.Entry)), Paid(g))).ToList();
        var byPlan = lines.GroupBy(l => (l.ProductName, l.PlanName, l.Entry.Currency))
            .Select(g => new PlanTotalDto(g.Key.ProductName, g.Key.PlanName, g.Key.Currency, g.Sum(l => Reports.Signed(l.Entry)), Paid(g)))
            .OrderByDescending(p => p.Net).ToList();
        var byProvider = lines.GroupBy(l => (l.Entry.Provider, l.Entry.Currency))
            .Select(g => new ProviderTotalDto(g.Key.Provider, g.Key.Currency, g.Sum(l => Reports.Signed(l.Entry)), Paid(g)))
            .OrderByDescending(p => p.Net).ToList();
        return new RevenueReportDto(query.From, query.To, totals, byMonth, byPlan, byProvider, truncated);
    }
}

internal sealed class LedgerExportHandler(ICommerceStore store, ICompanyDirectory companies) : IQueryHandler<LedgerExportQuery, Result<CsvFile>>
{
    public async Task<Result<CsvFile>> HandleAsync(LedgerExportQuery query, CancellationToken ct)
    {
        var (from, until) = Reports.Bounds(query.From, query.To);
        var lines = await store.LedgerAsync(from, until, Reports.MaxLines, ct);
        var names = await companies.FindManyAsync(lines.Select(l => l.CompanyId).Distinct().ToList(), ct);
        var csv = new StringBuilder("Date (UTC),Kind,Amount,Currency,Provider,Reference,Order,Company,Product,Plan\r\n");
        foreach (var line in lines)
        {
            var e = line.Entry;
            csv.AppendJoin(',', [
                e.At.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), e.Kind.ToString(),
                Reports.Signed(e).ToString("0.00", CultureInfo.InvariantCulture), e.Currency, Cell(e.Provider), Cell(e.Reference), Cell(line.OrderNumber),
                Cell(names.TryGetValue(line.CompanyId, out var c) ? c.Name : ""), Cell(line.ProductName), Cell(line.PlanName),
            ]).Append("\r\n");
        }
        return new CsvFile($"ledger-{query.From:yyyy-MM-dd}-to-{query.To:yyyy-MM-dd}.csv", csv.ToString());
    }

    /// <summary>
    /// Quotes a text cell, and puts a ' before anything a spreadsheet would run as a formula (=, +, -, @, tab, return), since
    /// company names and references come from customers and payment providers.
    /// </summary>
    internal static string Cell(string value)
    {
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0], StringComparison.Ordinal)) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
