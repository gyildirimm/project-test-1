using Microsoft.EntityFrameworkCore;
using TodoApi.Customers;
using TodoApi.Data;

namespace TodoApi.CustomerImports;

public sealed record ClassifiedRow(
    CsvRowInput Input,
    ImportRowStatus Status,
    string? Reason,
    int? DuplicateOfLineNumber,
    ValidCustomer? Valid);

public static class CustomerImportClassifier
{
    // Duplicate key: lowercased trimmed email when present, otherwise lowercased name + phone.
    // The two forms are prefixed so an email can never collide with a name/phone key.
    public static string KeyOf(string name, string? email, string? phone) =>
        string.IsNullOrEmpty(email)
            ? $"name-phone:{name.Trim().ToLowerInvariant()}\u001f{phone?.Trim().ToLowerInvariant()}"
            : $"email:{email.Trim().ToLowerInvariant()}";

    public static async Task<HashSet<string>> LoadExistingKeysAsync(CustomerDbContext db)
    {
        var customers = await db.Customers.AsNoTracking()
            .Select(c => new { c.Name, c.Email, c.Phone })
            .ToListAsync();
        return customers.Select(c => KeyOf(c.Name, c.Email, c.Phone)).ToHashSet();
    }

    // Invalid rows never take part in duplicate detection. Among valid rows, the first occurrence
    // of a key is Importable and later ones point back to it.
    public static List<ClassifiedRow> Classify(IEnumerable<CsvRowInput> rows, ISet<string> existingKeys)
    {
        var firstValidLine = new Dictionary<string, int>();
        var result = new List<ClassifiedRow>();

        foreach (var row in rows)
        {
            var valid = CustomerValidator.Validate(new CustomerRequest(row.Name, row.Email, row.Phone), out var errors);
            if (valid is null)
            {
                var reason = string.Join(" ", errors.Values.SelectMany(messages => messages));
                result.Add(new ClassifiedRow(row, ImportRowStatus.Invalid, reason, null, null));
                continue;
            }

            var key = KeyOf(valid.Name, valid.Email, valid.Phone);
            if (existingKeys.Contains(key))
            {
                result.Add(new ClassifiedRow(row, ImportRowStatus.AlreadyExists,
                    "A customer with the same email, or the same name and phone, already exists.", null, valid));
            }
            else if (firstValidLine.TryGetValue(key, out var firstLine))
            {
                result.Add(new ClassifiedRow(row, ImportRowStatus.DuplicateInFile,
                    $"Same customer as line {firstLine}.", firstLine, valid));
            }
            else
            {
                firstValidLine[key] = row.LineNumber;
                result.Add(new ClassifiedRow(row, ImportRowStatus.Importable, null, null, valid));
            }
        }

        return result;
    }
}
