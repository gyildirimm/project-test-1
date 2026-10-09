namespace Expenses.Api.Models;

public class Expense
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Amount { get; set; }
    public DateOnly Date { get; set; }
    public ExpenseStatus Status { get; set; } = ExpenseStatus.Draft;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public List<StatusHistoryEntry> StatusHistory { get; set; } = new();
}
