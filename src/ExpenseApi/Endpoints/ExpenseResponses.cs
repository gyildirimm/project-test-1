using System.Globalization;
using ExpenseApi.Models;

namespace ExpenseApi.Endpoints;

public sealed record ExpenseResponse(
    Guid Id,
    string Title,
    string? Description,
    decimal Amount,
    string Date,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? DecisionReason)
{
    public static ExpenseResponse From(Expense e) => new(
        e.Id,
        e.Title,
        e.Description,
        e.Amount,
        e.Date.ToString(ExpenseValidator.DateFormat, CultureInfo.InvariantCulture),
        e.Status.ToString(),
        e.CreatedAt,
        e.UpdatedAt,
        e.DecisionReason);
}

public sealed record ExpenseDetailResponse(
    Guid Id,
    string Title,
    string? Description,
    decimal Amount,
    string Date,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? DecisionReason,
    IReadOnlyList<StatusHistoryResponse> History)
{
    public static ExpenseDetailResponse From(Expense e)
    {
        var summary = ExpenseResponse.From(e);
        var history = e.History
            .OrderBy(h => h.Id)
            .Select(h => new StatusHistoryResponse(h.FromStatus?.ToString(), h.ToStatus.ToString(), h.At, h.Note))
            .ToList();

        return new(summary.Id, summary.Title, summary.Description, summary.Amount, summary.Date, summary.Status,
            summary.CreatedAt, summary.UpdatedAt, summary.DecisionReason, history);
    }
}

public sealed record StatusHistoryResponse(string? FromStatus, string ToStatus, DateTimeOffset At, string? Note);
