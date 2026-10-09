namespace ExpenseApi.Models;

public class Expense
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Amount { get; set; }
    public DateOnly Date { get; set; }
    public ExpenseStatus Status { get; private set; } = ExpenseStatus.Draft;

    /// <summary>Reason given when the expense is rejected. Not written by any endpoint yet.</summary>
    public string? DecisionReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<StatusHistoryEntry> History { get; set; } = [];

    public static Expense CreateDraft(string title, string? description, decimal amount, DateOnly date, DateTimeOffset now)
    {
        var expense = new Expense
        {
            Id = Guid.NewGuid(),
            Title = title,
            Description = description,
            Amount = amount,
            Date = date,
            Status = ExpenseStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now,
        };
        expense.History.Add(new StatusHistoryEntry
        {
            FromStatus = null,
            ToStatus = ExpenseStatus.Draft,
            At = now,
        });
        return expense;
    }

    public void Update(string title, string? description, decimal amount, DateOnly date, DateTimeOffset now)
    {
        Title = title;
        Description = description;
        Amount = amount;
        Date = date;
        UpdatedAt = now;
    }

    /// <summary>Moves to <paramref name="to"/> and appends a history entry. Returns false if the transition is not allowed.</summary>
    public bool TryTransition(ExpenseStatus to, DateTimeOffset now, string? note = null)
    {
        if (!ExpenseStatusRules.CanTransition(Status, to))
        {
            return false;
        }

        History.Add(new StatusHistoryEntry
        {
            FromStatus = Status,
            ToStatus = to,
            At = now,
            Note = note,
        });
        Status = to;
        UpdatedAt = now;
        return true;
    }
}
