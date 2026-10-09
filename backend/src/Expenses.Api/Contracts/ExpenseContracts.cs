using System.Text.Json.Serialization;

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

// Request body for POST /api/manager/expenses/{id}/reject. Reason must be non-blank.
public record RejectRequest(string? Reason);

// Response for GET /api/manager/summary. Counts always contains all four statuses, zeros included.
// JSON keys are the exact status names (Draft, Pending, ...), not camelCase.
public record ManagerSummary(ManagerStatusCounts Counts, decimal ApprovedTotal);

public record ManagerStatusCounts(
    [property: JsonPropertyName("Draft")] int Draft,
    [property: JsonPropertyName("Pending")] int Pending,
    [property: JsonPropertyName("Approved")] int Approved,
    [property: JsonPropertyName("Rejected")] int Rejected);
