using System.Globalization;
using System.Text.Json;

namespace ExpenseApi.Endpoints;

public sealed record ExpenseValues(string Title, string? Description, decimal Amount, DateOnly Date);

public sealed record ExpenseValidation(ExpenseValues? Values, Dictionary<string, string[]> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Reads create/update bodies from raw JSON so every problem (missing, wrong type, bad value)
/// is reported per field instead of failing deserialization with a generic 400.
/// </summary>
public static class ExpenseValidator
{
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 2000;
    public const string DateFormat = "yyyy-MM-dd";

    public static ExpenseValidation Validate(JsonElement body)
    {
        var errors = new Dictionary<string, List<string>>();

        if (body.ValueKind != JsonValueKind.Object)
        {
            return Fail(new Dictionary<string, List<string>> { ["Body"] = ["Request body must be a JSON object."] });
        }

        var title = ReadString(body, "title", "Title", errors);
        var description = ReadString(body, "description", "Description", errors);
        var amount = ReadAmount(body, errors);
        var date = ReadDate(body, errors);

        if (title is null)
        {
            Add(errors, "Title", "Title is required.");
        }
        else if (title.Length > TitleMaxLength)
        {
            Add(errors, "Title", $"Title must be at most {TitleMaxLength} characters.");
        }

        if (description is not null && description.Length > DescriptionMaxLength)
        {
            Add(errors, "Description", $"Description must be at most {DescriptionMaxLength} characters.");
        }

        if (amount is decimal value && value <= 0)
        {
            Add(errors, "Amount", "Amount must be greater than 0.");
        }

        if (errors.Count > 0 || title is null || amount is null || date is null)
        {
            return Fail(errors);
        }

        return new ExpenseValidation(
            new ExpenseValues(title, description, amount.Value, date.Value),
            new Dictionary<string, string[]>());
    }

    private static ExpenseValidation Fail(Dictionary<string, List<string>> errors) =>
        new(null, errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()));

    private static void Add(Dictionary<string, List<string>> errors, string field, string message)
    {
        if (!errors.TryGetValue(field, out var list))
        {
            errors[field] = list = [];
        }
        list.Add(message);
    }

    private static JsonElement? Find(JsonElement body, string name)
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

    // Returns the trimmed string, or null when absent/empty. Non-string values are reported as errors.
    private static string? ReadString(JsonElement body, string jsonName, string field, Dictionary<string, List<string>> errors)
    {
        var element = Find(body, jsonName);
        if (element is null || element.Value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (element.Value.ValueKind != JsonValueKind.String)
        {
            Add(errors, field, $"{field} must be a string.");
            return null;
        }
        var text = element.Value.GetString()?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static decimal? ReadAmount(JsonElement body, Dictionary<string, List<string>> errors)
    {
        var element = Find(body, "amount");
        if (element is null || element.Value.ValueKind == JsonValueKind.Null)
        {
            Add(errors, "Amount", "Amount is required.");
            return null;
        }
        if (element.Value.ValueKind != JsonValueKind.Number || !element.Value.TryGetDecimal(out var amount))
        {
            Add(errors, "Amount", "Amount must be a number.");
            return null;
        }
        return amount;
    }

    private static DateOnly? ReadDate(JsonElement body, Dictionary<string, List<string>> errors)
    {
        var element = Find(body, "date");
        if (element is null || element.Value.ValueKind == JsonValueKind.Null)
        {
            Add(errors, "Date", "Date is required.");
            return null;
        }
        if (element.Value.ValueKind != JsonValueKind.String
            || !DateOnly.TryParseExact(element.Value.GetString(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            Add(errors, "Date", "Date must be a valid date in YYYY-MM-DD format.");
            return null;
        }
        return date;
    }
}
