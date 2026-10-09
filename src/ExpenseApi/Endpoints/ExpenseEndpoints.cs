using System.Text.Json;
using ExpenseApi.Data;
using ExpenseApi.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseApi.Endpoints;

public static class ExpenseEndpoints
{
    public static IEndpointRouteBuilder MapExpenseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/expenses");

        group.MapGet("", async (string? status, ExpenseDbContext db) =>
        {
            IQueryable<Expense> query = db.Expenses.AsNoTracking();

            if (status is not null)
            {
                if (!Enum.GetNames<ExpenseStatus>().Contains(status, StringComparer.Ordinal))
                {
                    return Errors("Status", "Status must be one of: Draft, Submitted, Approved, Rejected.");
                }
                var parsed = Enum.Parse<ExpenseStatus>(status);
                query = query.Where(e => e.Status == parsed);
            }

            var items = await query
                .OrderByDescending(e => e.CreatedAt)
                .ThenBy(e => e.Id)
                .Select(e => ExpenseResponse.From(e))
                .ToListAsync();
            return Results.Ok(items);
        });

        group.MapGet("/{id:guid}", async (Guid id, ExpenseDbContext db) =>
        {
            var expense = await db.Expenses.AsNoTracking()
                .Include(e => e.History)
                .FirstOrDefaultAsync(e => e.Id == id);
            return expense is null ? NotFoundFor(id) : Results.Ok(ExpenseDetailResponse.From(expense));
        });

        group.MapPost("", async (JsonElement body, ExpenseDbContext db) =>
        {
            var validation = ExpenseValidator.Validate(body);
            if (!validation.IsValid)
            {
                return Results.BadRequest(new { errors = validation.Errors });
            }

            var v = validation.Values!;
            var expense = Expense.CreateDraft(v.Title, v.Description, v.Amount, v.Date, DateTimeOffset.UtcNow);
            db.Expenses.Add(expense);
            await db.SaveChangesAsync();

            return Results.Created($"/api/expenses/{expense.Id}", ExpenseResponse.From(expense));
        });

        group.MapPut("/{id:guid}", async (Guid id, JsonElement body, ExpenseDbContext db) =>
        {
            var expense = await db.Expenses.FirstOrDefaultAsync(e => e.Id == id);
            if (expense is null)
            {
                return NotFoundFor(id);
            }
            if (!ExpenseStatusRules.IsEditable(expense.Status))
            {
                return NotDraft(expense, "edited");
            }

            var validation = ExpenseValidator.Validate(body);
            if (!validation.IsValid)
            {
                return Results.BadRequest(new { errors = validation.Errors });
            }

            var v = validation.Values!;
            expense.Update(v.Title, v.Description, v.Amount, v.Date, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();

            return Results.Ok(ExpenseResponse.From(expense));
        });

        group.MapDelete("/{id:guid}", async (Guid id, ExpenseDbContext db) =>
        {
            var expense = await db.Expenses.FirstOrDefaultAsync(e => e.Id == id);
            if (expense is null)
            {
                return NotFoundFor(id);
            }
            if (!ExpenseStatusRules.IsDeletable(expense.Status))
            {
                return NotDraft(expense, "deleted");
            }

            db.Expenses.Remove(expense);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        group.MapPost("/{id:guid}/submit", async (Guid id, ExpenseDbContext db) =>
        {
            var expense = await db.Expenses.Include(e => e.History).FirstOrDefaultAsync(e => e.Id == id);
            if (expense is null)
            {
                return NotFoundFor(id);
            }

            var from = expense.Status;
            if (!expense.TryTransition(ExpenseStatus.Submitted, DateTimeOffset.UtcNow))
            {
                return Results.Conflict(new { message = $"Expense cannot be submitted from status {from}." });
            }

            await db.SaveChangesAsync();
            return Results.Ok(ExpenseDetailResponse.From(expense));
        });

        return app;
    }

    private static IResult Errors(string field, string message) =>
        Results.BadRequest(new { errors = new Dictionary<string, string[]> { [field] = [message] } });

    private static IResult NotFoundFor(Guid id) =>
        Results.NotFound(new { message = $"Expense {id} was not found." });

    private static IResult NotDraft(Expense expense, string action) =>
        Results.Conflict(new { message = $"Only Draft expenses can be {action}; this expense is {expense.Status}." });
}
