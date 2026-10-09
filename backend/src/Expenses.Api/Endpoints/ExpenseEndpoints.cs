using Expenses.Api.Contracts;
using Expenses.Api.Data;
using Expenses.Api.Models;
using Expenses.Api.Validation;
using Microsoft.EntityFrameworkCore;

namespace Expenses.Api.Endpoints;

public static class ExpenseEndpoints
{
    public static void MapExpenseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/expenses");

        group.MapPost("/", async (ExpenseRequest request, ExpensesDbContext db, CancellationToken ct) =>
        {
            if (!ExpenseValidator.TryValidate(request, out var v, out var errors))
                return Results.ValidationProblem(errors);

            var now = DateTimeOffset.UtcNow;
            var expense = new Expense
            {
                Title = v!.Title,
                Description = v.Description,
                Amount = v.Amount,
                Date = v.Date,
                Status = ExpenseStatus.Draft,
                CreatedAt = now,
                UpdatedAt = now
            };
            expense.StatusHistory.Add(new StatusHistoryEntry
            {
                FromStatus = null,
                ToStatus = ExpenseStatus.Draft,
                Reason = "Created",
                OccurredAt = now
            });

            db.Expenses.Add(expense);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/expenses/{expense.Id}", ToDetail(expense));
        });

        group.MapGet("/", async (string? status, ExpensesDbContext db, CancellationToken ct) =>
        {
            var query = db.Expenses.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!Enum.TryParse<ExpenseStatus>(status, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                    return InvalidStatus(status);
                query = query.Where(e => e.Status == parsed);
            }

            var items = await query
                .OrderBy(e => e.Id)
                .Select(e => new ExpenseSummary(
                    e.Id, e.Title, e.Description, e.Amount, e.Date,
                    e.Status.ToString(), e.CreatedAt, e.UpdatedAt))
                .ToListAsync(ct);

            return Results.Ok(items);
        });

        group.MapGet("/{id:int}", async (int id, ExpensesDbContext db, CancellationToken ct) =>
        {
            var expense = await db.Expenses
                .AsNoTracking()
                .Include(e => e.StatusHistory)
                .FirstOrDefaultAsync(e => e.Id == id, ct);

            return expense is null ? NotFoundProblem(id) : Results.Ok(ToDetail(expense));
        });

        group.MapPut("/{id:int}", async (int id, ExpenseRequest request, ExpensesDbContext db, CancellationToken ct) =>
        {
            var expense = await db.Expenses.FirstOrDefaultAsync(e => e.Id == id, ct);
            if (expense is null)
                return NotFoundProblem(id);

            if (!ExpenseValidator.TryValidate(request, out var v, out var errors))
                return Results.ValidationProblem(errors);

            if (expense.Status != ExpenseStatus.Draft)
                return NotDraftConflict(id, expense.Status);

            expense.Title = v!.Title;
            expense.Description = v.Description;
            expense.Amount = v.Amount;
            expense.Date = v.Date;
            expense.UpdatedAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(ct);
            await db.Entry(expense).Collection(e => e.StatusHistory).LoadAsync(ct);

            return Results.Ok(ToDetail(expense));
        });

        group.MapDelete("/{id:int}", async (int id, ExpensesDbContext db, CancellationToken ct) =>
        {
            var expense = await db.Expenses.FirstOrDefaultAsync(e => e.Id == id, ct);
            if (expense is null)
                return NotFoundProblem(id);

            if (expense.Status != ExpenseStatus.Draft)
                return NotDraftConflict(id, expense.Status);

            db.Expenses.Remove(expense);
            await db.SaveChangesAsync(ct);

            return Results.NoContent();
        });

        group.MapPost("/{id:int}/submit", async (int id, ExpensesDbContext db, CancellationToken ct) =>
        {
            var expense = await db.Expenses.FirstOrDefaultAsync(e => e.Id == id, ct);
            if (expense is null)
                return NotFoundProblem(id);

            if (expense.Status != ExpenseStatus.Draft)
                return Results.Problem(
                    title: "Conflict",
                    detail: $"Expense {id} can only be submitted from Draft; current status is {expense.Status}.",
                    statusCode: StatusCodes.Status409Conflict);

            var now = DateTimeOffset.UtcNow;
            expense.Status = ExpenseStatus.Pending;
            expense.UpdatedAt = now;
            expense.StatusHistory.Add(new StatusHistoryEntry
            {
                FromStatus = ExpenseStatus.Draft,
                ToStatus = ExpenseStatus.Pending,
                Reason = null,
                OccurredAt = now
            });

            await db.SaveChangesAsync(ct);
            await db.Entry(expense).Collection(e => e.StatusHistory).LoadAsync(ct);

            return Results.Ok(ToDetail(expense));
        });
    }

    internal static ExpenseDetail ToDetail(Expense e) => new(
        e.Id, e.Title, e.Description, e.Amount, e.Date,
        e.Status.ToString(), e.CreatedAt, e.UpdatedAt,
        e.StatusHistory
            .OrderBy(h => h.Id)
            .Select(h => new StatusHistoryItem(
                h.FromStatus?.ToString(), h.ToStatus.ToString(), h.Reason, h.OccurredAt))
            .ToList());

    internal static IResult NotFoundProblem(int id) => Results.Problem(
        title: "Not Found",
        detail: $"Expense {id} was not found.",
        statusCode: StatusCodes.Status404NotFound);

    private static IResult NotDraftConflict(int id, ExpenseStatus status) => Results.Problem(
        title: "Conflict",
        detail: $"Expense {id} is {status}; only Draft expenses can be modified or deleted.",
        statusCode: StatusCodes.Status409Conflict);

    private static IResult InvalidStatus(string status) => Results.ValidationProblem(
        new Dictionary<string, string[]>
        {
            ["status"] = [$"'{status}' is not a valid status. Use Draft, Pending, Approved or Rejected."]
        });
}
