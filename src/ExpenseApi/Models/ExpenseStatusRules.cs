namespace ExpenseApi.Models;

/// <summary>Allowed status transitions and the edit/delete rules derived from status.</summary>
public static class ExpenseStatusRules
{
    // Submitted -> Approved/Rejected is part of the model but has no endpoint yet.
    private static readonly IReadOnlyDictionary<ExpenseStatus, ExpenseStatus[]> Transitions =
        new Dictionary<ExpenseStatus, ExpenseStatus[]>
        {
            [ExpenseStatus.Draft] = [ExpenseStatus.Submitted],
            [ExpenseStatus.Submitted] = [ExpenseStatus.Approved, ExpenseStatus.Rejected],
            [ExpenseStatus.Approved] = [],
            [ExpenseStatus.Rejected] = [],
        };

    public static bool CanTransition(ExpenseStatus from, ExpenseStatus to) =>
        Transitions[from].Contains(to);

    public static bool IsEditable(ExpenseStatus status) => status == ExpenseStatus.Draft;

    public static bool IsDeletable(ExpenseStatus status) => status == ExpenseStatus.Draft;
}
