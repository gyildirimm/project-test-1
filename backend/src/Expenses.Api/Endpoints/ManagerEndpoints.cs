using Expenses.Api.Contracts;
using Expenses.Api.Data;
using Expenses.Api.Models;
using Expenses.Api.Validation;
using Microsoft.EntityFrameworkCore;

namespace Expenses.Api.Endpoints;

public static class ManagerEndpoints
{
    public static void MapManagerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/manager");

        group.MapGet("/expenses/pending", async (ExpensesDbContext db, CancellationToken ct) =>
        {
            var items = await db.Expenses
                .AsNoTracking()
                .Include(e => e.StatusHistory)
                .Where(e => e.Status == ExpenseStatus.Pending)
                .OrderBy(e => e.Id)
                .ToListAsync(ct);

            return Results.Ok(items.Select(ExpenseEndpoints.ToDetail));
        });

        group.MapPost("/expenses/{id:int}/approve", async (int id, ExpensesDbContext db, CancellationToken ct) =>
        {
            var expense = await db.Expenses.FirstOrDefaultAsync(e => e.Id == id, ct);
            if (expense is null)
                return ExpenseEndpoints.NotFoundProblem(id);

            if (expense.Status != ExpenseStatus.Pending)
                return NotPendingConflict(id, expense.Status, "approved");

            var now = DateTimeOffset.UtcNow;
            expense.Status = ExpenseStatus.Approved;
            expense.UpdatedAt = now;
            expense.StatusHistory.Add(new StatusHistoryEntry
            {
                FromStatus = ExpenseStatus.Pending,
                ToStatus = ExpenseStatus.Approved,
                Reason = null,
                OccurredAt = now
            });

            await db.SaveChangesAsync(ct);
            await db.Entry(expense).Collection(e => e.StatusHistory).LoadAsync(ct);

            return Results.Ok(ExpenseEndpoints.ToDetail(expense));
        });

        group.MapPost("/expenses/{id:int}/reject", async (int id, RejectRequest? request, ExpensesDbContext db, CancellationToken ct) =>
        {
            var expense = await db.Expenses.FirstOrDefaultAsync(e => e.Id == id, ct);
            if (expense is null)
                return ExpenseEndpoints.NotFoundProblem(id);

            if (!ExpenseValidator.TryValidateRejectReason(request, out var reason, out var errors))
                return Results.ValidationProblem(errors);

            if (expense.Status != ExpenseStatus.Pending)
                return NotPendingConflict(id, expense.Status, "rejected");

            var now = DateTimeOffset.UtcNow;
            expense.Status = ExpenseStatus.Rejected;
            expense.UpdatedAt = now;
            expense.StatusHistory.Add(new StatusHistoryEntry
            {
                FromStatus = ExpenseStatus.Pending,
                ToStatus = ExpenseStatus.Rejected,
                Reason = reason,
                OccurredAt = now
            });

            await db.SaveChangesAsync(ct);
            await db.Entry(expense).Collection(e => e.StatusHistory).LoadAsync(ct);

            return Results.Ok(ExpenseEndpoints.ToDetail(expense));
        });

        group.MapGet("/summary", async (ExpensesDbContext db, CancellationToken ct) =>
        {
            var grouped = await db.Expenses
                .AsNoTracking()
                .GroupBy(e => e.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            // Amounts are summed in memory: EF's SQLite provider cannot translate Sum over decimal.
            var approvedAmounts = await db.Expenses
                .AsNoTracking()
                .Where(e => e.Status == ExpenseStatus.Approved)
                .Select(e => e.Amount)
                .ToListAsync(ct);

            int CountOf(ExpenseStatus status) => grouped.FirstOrDefault(g => g.Status == status)?.Count ?? 0;

            var counts = new ManagerStatusCounts(
                CountOf(ExpenseStatus.Draft),
                CountOf(ExpenseStatus.Pending),
                CountOf(ExpenseStatus.Approved),
                CountOf(ExpenseStatus.Rejected));

            return Results.Ok(new ManagerSummary(counts, approvedAmounts.Sum()));
        });
    }

    private static IResult NotPendingConflict(int id, ExpenseStatus status, string target) => Results.Problem(
        title: "Conflict",
        detail: $"Expense {id} is {status}; only Pending expenses can be {target}.",
        statusCode: StatusCodes.Status409Conflict);
}
