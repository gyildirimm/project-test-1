using System.Globalization;
using System.Text.Json;
using ExpenseApi.Data;
using ExpenseApi.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseApi.Endpoints;

/// <summary>Manager approval flow on top of the expense endpoints. There is no authentication yet, so every decision is recorded under the same manager actor.</summary>
public static class ApprovalEndpoints
{
    public const string ManagerActor = "manager";
    public const string ManagerRole = "Manager";

    private const string NoteField = "Note";
    private const string ReasonField = "Reason";

    public static IEndpointRouteBuilder MapApprovalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/approvals");

        group.MapGet("/pending", async (ExpenseDbContext db) =>
        {
            var submitted = await db.Expenses.AsNoTracking()
                .Where(e => e.Status == ExpenseStatus.Submitted)
                .Include(e => e.History)
                .ToListAsync();

            // Oldest submission first, so the review queue is first-in, first-out.
            var items = submitted
                .Select(e => new PendingApprovalResponse(
                    e.Id,
                    e.Title,
                    e.Amount,
                    e.Date.ToString(ExpenseValidator.DateFormat, CultureInfo.InvariantCulture),
                    SubmittedAt(e)))
                .OrderBy(p => p.SubmittedAt)
                .ThenBy(p => p.Id)
                .ToList();
            return Results.Ok(items);
        });

        group.MapGet("/summary", async (ExpenseDbContext db) =>
        {
            // Counts are grouped in SQL. Sum over decimal is not translatable by the SQLite provider, so amounts are summed in memory.
            var counts = await db.Expenses.AsNoTracking()
                .GroupBy(e => e.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            var approvedAmounts = await db.Expenses.AsNoTracking()
                .Where(e => e.Status == ExpenseStatus.Approved)
                .Select(e => e.Amount)
                .ToListAsync();

            var countsByStatus = Enum.GetValues<ExpenseStatus>()
                .ToDictionary(s => s.ToString(), s => counts.FirstOrDefault(c => c.Status == s)?.Count ?? 0);

            return Results.Ok(new ApprovalSummaryResponse(countsByStatus, approvedAmounts.Sum()));
        });

        group.MapGet("/{id:guid}", async (Guid id, ExpenseDbContext db) =>
        {
            var expense = await db.Expenses.AsNoTracking()
                .Include(e => e.History)
                .FirstOrDefaultAsync(e => e.Id == id);
            return expense is null ? NotFoundFor(id) : Results.Ok(ExpenseDetailResponse.From(expense));
        });

        group.MapPost("/{id:guid}/approve", async (Guid id, HttpRequest request, ExpenseDbContext db) =>
        {
            var body = await ReadBodyAsync(request);
            var expense = await db.Expenses.Include(e => e.History).FirstOrDefaultAsync(e => e.Id == id);
            if (expense is null)
            {
                return NotFoundFor(id);
            }
            if (!ExpenseStatusRules.CanTransition(expense.Status, ExpenseStatus.Approved))
            {
                return NotSubmitted(expense, "approved");
            }

            var (note, errors) = ReadOptionalText(body, NoteField, 2000);
            if (errors is not null)
            {
                return Results.BadRequest(new { errors });
            }

            // The transition was checked above, so this cannot fail.
            expense.TryTransition(ExpenseStatus.Approved, DateTimeOffset.UtcNow, note, ManagerActor, ManagerRole);
            await db.SaveChangesAsync();
            return Results.Ok(ExpenseDetailResponse.From(expense));
        });

        group.MapPost("/{id:guid}/reject", async (Guid id, HttpRequest request, ExpenseDbContext db) =>
        {
            var body = await ReadBodyAsync(request);
            var expense = await db.Expenses.Include(e => e.History).FirstOrDefaultAsync(e => e.Id == id);
            if (expense is null)
            {
                return NotFoundFor(id);
            }
            if (!ExpenseStatusRules.CanTransition(expense.Status, ExpenseStatus.Rejected))
            {
                return NotSubmitted(expense, "rejected");
            }

            var (reason, errors) = ReadRequiredReason(body);
            if (errors is not null)
            {
                return Results.BadRequest(new { errors });
            }

            expense.TryTransition(ExpenseStatus.Rejected, DateTimeOffset.UtcNow, null, ManagerActor, ManagerRole);
            expense.DecisionReason = reason;
            await db.SaveChangesAsync();
            return Results.Ok(ExpenseDetailResponse.From(expense));
        });

        return app;
    }

    // Reads the body by hand: model binding a JsonElement? rejects an empty body with 400, but approve may be sent with no body.
    // Malformed JSON syntax still gets ASP.NET's default 400 from ReadFromJsonAsync.
    private static async Task<JsonElement?> ReadBodyAsync(HttpRequest request)
    {
        if (request.ContentLength is null or 0)
        {
            return null;
        }
        return await request.ReadFromJsonAsync<JsonElement>();
    }

    private static DateTimeOffset SubmittedAt(Expense expense) =>
        expense.History
            .Where(h => h.ToStatus == ExpenseStatus.Submitted)
            .Select(h => h.At)
            .DefaultIfEmpty(expense.UpdatedAt)
            .Max();

    // Body is optional for approve. When present it must be a JSON object; "note" is optional and blank becomes null.
    private static (string? Value, Dictionary<string, string[]>? Errors) ReadOptionalText(JsonElement? body, string field, int maxLength)
    {
        if (body is null || body.Value.ValueKind == JsonValueKind.Null)
        {
            return (null, null);
        }
        if (body.Value.ValueKind != JsonValueKind.Object)
        {
            return (null, Single("Body", "Request body must be a JSON object."));
        }

        var property = FindProperty(body.Value, field);
        if (property is null || property.Value.ValueKind == JsonValueKind.Null)
        {
            return (null, null);
        }
        if (property.Value.ValueKind != JsonValueKind.String)
        {
            return (null, Single(field, $"{field} must be a string."));
        }

        var text = property.Value.GetString()?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return (null, null);
        }
        if (text.Length > maxLength)
        {
            return (null, Single(field, $"{field} must be at most {maxLength} characters."));
        }
        return (text, null);
    }

    // Reject requires a non-blank reason. A missing body counts as a missing reason.
    private static (string Value, Dictionary<string, string[]>? Errors) ReadRequiredReason(JsonElement? body)
    {
        if (body is null || body.Value.ValueKind == JsonValueKind.Null)
        {
            return (string.Empty, Single(ReasonField, "Reason is required."));
        }
        if (body.Value.ValueKind != JsonValueKind.Object)
        {
            return (string.Empty, Single("Body", "Request body must be a JSON object."));
        }

        var property = FindProperty(body.Value, "reason");
        if (property is null || property.Value.ValueKind == JsonValueKind.Null)
        {
            return (string.Empty, Single(ReasonField, "Reason is required."));
        }
        if (property.Value.ValueKind != JsonValueKind.String)
        {
            return (string.Empty, Single(ReasonField, "Reason must be a string."));
        }

        var text = property.Value.GetString()?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return (string.Empty, Single(ReasonField, "Reason is required."));
        }
        if (text.Length > 2000)
        {
            return (string.Empty, Single(ReasonField, "Reason must be at most 2000 characters."));
        }
        return (text, null);
    }

    private static JsonElement? FindProperty(JsonElement body, string name)
    {
        foreach (var property in body.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }
        return null;
    }

    private static Dictionary<string, string[]> Single(string field, string message) =>
        new() { [field] = [message] };

    private static IResult NotFoundFor(Guid id) =>
        Results.NotFound(new { message = $"Expense {id} was not found." });

    private static IResult NotSubmitted(Expense expense, string action) =>
        Results.Conflict(new { message = $"Only Submitted expenses can be {action}; this expense is {expense.Status}." });
}

public sealed record PendingApprovalResponse(Guid Id, string Title, decimal Amount, string Date, DateTimeOffset SubmittedAt);

public sealed record ApprovalSummaryResponse(IReadOnlyDictionary<string, int> CountsByStatus, decimal ApprovedTotal);
