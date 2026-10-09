namespace ExpenseApi.Models;

/// <summary>Permanent record of one status change. FromStatus is null for the creation entry.</summary>
public class StatusHistoryEntry
{
    // No authentication yet: every change is attributed to the same default actor.
    public const string DefaultActor = "employee";
    public const string DefaultRole = "Employee";

    public int Id { get; set; }
    public Guid ExpenseId { get; set; }
    public ExpenseStatus? FromStatus { get; set; }
    public ExpenseStatus ToStatus { get; set; }
    public DateTimeOffset At { get; set; }
    public string Actor { get; set; } = DefaultActor;
    public string Role { get; set; } = DefaultRole;
    public string? Note { get; set; }
}
