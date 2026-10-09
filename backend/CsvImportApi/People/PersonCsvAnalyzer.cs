using System.Text.RegularExpressions;
using CsvImportApi.Csv;

namespace CsvImportApi.People;

/// <summary>
/// Pure validation of a person CSV: header rules, per-row validation, and dedupe.
/// Does not touch storage; existence checks are injected via <c>emailExists</c>.
/// </summary>
public static partial class PersonCsvAnalyzer
{
    public static IReadOnlyList<ImportRowDto> Analyze(string csvText, Func<string, bool> emailExists)
    {
        IReadOnlyList<CsvRecord> records;
        try
        {
            records = CsvParser.Parse(csvText);
        }
        catch (CsvParseException ex)
        {
            throw new ImportException(400, ImportErrorCodes.Malformed, ex.Message,
                [new { line = ex.LineNumber }]);
        }

        var nonBlank = records.Where(r => !IsBlank(r)).ToList();
        if (nonBlank.Count == 0)
        {
            throw new ImportException(400, ImportErrorCodes.EmptyFile, "CSV file has no header row.");
        }

        var header = nonBlank[0];
        var columns = ReadHeader(header);
        var dataRows = nonBlank.Skip(1).ToList();

        if (dataRows.Count > PersonCsvContract.MaxDataRows)
        {
            throw new ImportException(422, ImportErrorCodes.TooManyRows,
                $"CSV has {dataRows.Count} data rows; the maximum is {PersonCsvContract.MaxDataRows}.");
        }

        var rows = new List<ImportRowDto>(dataRows.Count);
        var seenEmails = new HashSet<string>(StringComparer.Ordinal);

        foreach (var record in dataRows)
        {
            var errors = new List<RowErrorDto>();
            if (record.Fields.Count != header.Fields.Count)
            {
                errors.Add(new RowErrorDto(RowErrorCodes.FieldCountMismatch, null,
                    $"Expected {header.Fields.Count} fields, found {record.Fields.Count}."));
            }

            var firstName = Field(record, columns, PersonCsvContract.FirstName);
            var lastName = Field(record, columns, PersonCsvContract.LastName);
            var email = Field(record, columns, PersonCsvContract.Email);
            var phone = columns.ContainsKey(PersonCsvContract.Phone)
                ? Field(record, columns, PersonCsvContract.Phone)
                : null;
            if (string.IsNullOrEmpty(phone)) phone = null;

            ValidateName(PersonCsvContract.FirstName, firstName, errors);
            ValidateName(PersonCsvContract.LastName, lastName, errors);
            ValidateEmail(email, errors);
            ValidatePhone(phone, errors);

            var values = new ImportRowValues(firstName, lastName, email, phone);

            if (errors.Count > 0)
            {
                rows.Add(new ImportRowDto(record.LineNumber, ImportRowStatus.Invalid, values, errors));
                continue;
            }

            var key = PersonCsvContract.NormalizeEmail(email);
            string status;
            if (emailExists(key))
            {
                status = ImportRowStatus.AlreadyExists;
            }
            else if (!seenEmails.Add(key))
            {
                status = ImportRowStatus.DuplicateInFile;
            }
            else
            {
                status = ImportRowStatus.Valid;
            }

            rows.Add(new ImportRowDto(record.LineNumber, status, values, []));
        }

        return rows;
    }

    private static Dictionary<string, int> ReadHeader(CsvRecord header)
    {
        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        var unknown = new List<object>();

        for (var i = 0; i < header.Fields.Count; i++)
        {
            var name = header.Fields[i].Trim().ToLowerInvariant();

            if (!PersonCsvContract.AllowedColumns.Contains(name))
            {
                unknown.Add(new { line = header.LineNumber, column = header.Fields[i].Trim() });
                continue;
            }

            if (!columns.TryAdd(name, i))
            {
                throw new ImportException(400, ImportErrorCodes.DuplicateColumn,
                    $"Column '{name}' appears more than once in the header.",
                    [new { line = header.LineNumber, column = name }]);
            }
        }

        if (unknown.Count > 0)
        {
            throw new ImportException(400, ImportErrorCodes.UnknownColumn,
                "CSV header contains columns that are not part of the contract.", unknown);
        }

        var missing = PersonCsvContract.RequiredColumns.Where(c => !columns.ContainsKey(c)).ToList();
        if (missing.Count > 0)
        {
            throw new ImportException(400, ImportErrorCodes.MissingColumn,
                "CSV header is missing required columns.",
                missing.Select(c => (object)new { column = c }).ToList());
        }

        return columns;
    }

    private static string Field(CsvRecord record, Dictionary<string, int> columns, string name)
    {
        if (!columns.TryGetValue(name, out var index) || index >= record.Fields.Count)
        {
            return string.Empty;
        }

        return record.Fields[index].Trim();
    }

    private static void ValidateName(string field, string value, List<RowErrorDto> errors)
    {
        if (value.Length == 0)
        {
            errors.Add(new RowErrorDto(RowErrorCodes.Required, field, $"'{field}' is required."));
        }
        else if (value.Length > PersonCsvContract.MaxNameLength)
        {
            errors.Add(new RowErrorDto(RowErrorCodes.TooLong, field,
                $"'{field}' must be at most {PersonCsvContract.MaxNameLength} characters."));
        }
    }

    private static void ValidateEmail(string value, List<RowErrorDto> errors)
    {
        if (value.Length == 0)
        {
            errors.Add(new RowErrorDto(RowErrorCodes.Required, PersonCsvContract.Email, "'email' is required."));
        }
        else if (value.Length > PersonCsvContract.MaxEmailLength)
        {
            errors.Add(new RowErrorDto(RowErrorCodes.TooLong, PersonCsvContract.Email,
                $"'email' must be at most {PersonCsvContract.MaxEmailLength} characters."));
        }
        else if (!EmailPattern().IsMatch(value))
        {
            errors.Add(new RowErrorDto(RowErrorCodes.InvalidEmail, PersonCsvContract.Email,
                "'email' is not a valid address."));
        }
    }

    private static void ValidatePhone(string? value, List<RowErrorDto> errors)
    {
        if (value is null) return;

        if (value.Length > PersonCsvContract.MaxPhoneLength)
        {
            errors.Add(new RowErrorDto(RowErrorCodes.TooLong, PersonCsvContract.Phone,
                $"'phone' must be at most {PersonCsvContract.MaxPhoneLength} characters."));
            return;
        }

        var digits = value.Count(char.IsAsciiDigit);
        if (!PhonePattern().IsMatch(value) || digits < 7 || digits > 15)
        {
            errors.Add(new RowErrorDto(RowErrorCodes.InvalidPhone, PersonCsvContract.Phone,
                "'phone' must contain 7-15 digits and only digits, spaces, '+', '-', '(' or ')'."));
        }
    }

    private static bool IsBlank(CsvRecord record) => record.Fields.All(f => string.IsNullOrWhiteSpace(f));

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"^\+?[0-9 ()\-]+$")]
    private static partial Regex PhonePattern();
}
