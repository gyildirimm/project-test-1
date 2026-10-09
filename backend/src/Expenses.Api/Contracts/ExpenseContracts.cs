namespace Expenses.Api.Contracts;

// Request body for POST and PUT. Date is a raw string so that invalid values
// produce a field-level 400 instead of a framework JSON binding error.
public record ExpenseRequest(string? Title, string? Description, decimal Amount, string? Date);

// Response for GET /api/expenses (list). Status is the enum name: Draft|Pending|Approved|Rejected.
public record ExpenseSummary(
    int Id,
    string Title,
    string? Description,
    decimal Amount,
    DateOnly Date,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

// Response for POST, GET {id}, PUT and submit. History is ordered by Id (oldest first).
public record ExpenseDetail(
    int Id,
    string Title,
    string? Description,
    decimal Amount,
    DateOnly Date,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<StatusHistoryItem> History);

public record StatusHistoryItem(
    string? FromStatus,
    string ToStatus,
    string? Reason,
    DateTimeOffset OccurredAt);
