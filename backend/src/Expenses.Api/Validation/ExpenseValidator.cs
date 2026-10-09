using System.Globalization;
using Expenses.Api.Contracts;

namespace Expenses.Api.Validation;

public record ValidExpense(string Title, string? Description, decimal Amount, DateOnly Date);

public static class ExpenseValidator
{
    public static bool TryValidate(
        ExpenseRequest request,
        out ValidExpense? value,
        out Dictionary<string, string[]> errors)
    {
        var found = new Dictionary<string, List<string>>();

        var title = request.Title?.Trim();
        if (string.IsNullOrEmpty(title))
            Add(found, "title", "Title is required.");
        else if (title.Length > 200)
            Add(found, "title", "Title must be at most 200 characters.");

        var description = request.Description?.Trim();
        if (description?.Length > 2000)
            Add(found, "description", "Description must be at most 2000 characters.");

        if (request.Amount <= 0)
            Add(found, "amount", "Amount must be greater than 0.");

        var date = default(DateOnly);
        if (string.IsNullOrWhiteSpace(request.Date))
            Add(found, "date", "Date is required.");
        else if (!DateOnly.TryParseExact(request.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            Add(found, "date", "Date must be a valid date in yyyy-MM-dd format.");

        errors = found.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
        if (errors.Count > 0)
        {
            value = null;
            return false;
        }

        value = new ValidExpense(
            title!,
            string.IsNullOrEmpty(description) ? null : description,
            request.Amount,
            date);
        return true;
    }

    private static void Add(Dictionary<string, List<string>> errors, string field, string message)
    {
        if (!errors.TryGetValue(field, out var list))
            errors[field] = list = new List<string>();
        list.Add(message);
    }
}
