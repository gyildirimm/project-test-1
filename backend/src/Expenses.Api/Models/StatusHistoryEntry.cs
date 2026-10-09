namespace Expenses.Api.Models;

public class StatusHistoryEntry
{
    public int Id { get; set; }
    public int ExpenseId { get; set; }
    public Expense Expense { get; set; } = null!;
    public ExpenseStatus? FromStatus { get; set; }
    public ExpenseStatus ToStatus { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
