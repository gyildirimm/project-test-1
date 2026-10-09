namespace CsvImportApi.People;

/// <summary>Fixed CSV contract for person imports. Changing any value here is a breaking API change.</summary>
public static class PersonCsvContract
{
    public const string FirstName = "first_name";
    public const string LastName = "last_name";
    public const string Email = "email";
    public const string Phone = "phone";

    public static readonly string[] RequiredColumns = [FirstName, LastName, Email];
    public static readonly string[] AllowedColumns = [FirstName, LastName, Email, Phone];

    public const int MaxFileBytes = 1024 * 1024;
    public const int MaxDataRows = 1000;
    public const int MaxNameLength = 100;
    public const int MaxEmailLength = 254;
    public const int MaxPhoneLength = 30;

    public const string FormFieldName = "file";

    public static readonly TimeSpan PreviewTtl = TimeSpan.FromMinutes(15);

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}

public static class ImportRowStatus
{
    public const string Valid = "valid";
    public const string Invalid = "invalid";
    public const string DuplicateInFile = "duplicate_in_file";
    public const string AlreadyExists = "already_exists";
}

public static class ImportErrorCodes
{
    public const string FileMissing = "CSV_FILE_MISSING";
    public const string EmptyFile = "CSV_EMPTY_FILE";
    public const string FileTooLarge = "CSV_FILE_TOO_LARGE";
    public const string InvalidEncoding = "CSV_INVALID_ENCODING";
    public const string Malformed = "CSV_MALFORMED";
    public const string MissingColumn = "CSV_MISSING_COLUMN";
    public const string UnknownColumn = "CSV_UNKNOWN_COLUMN";
    public const string DuplicateColumn = "CSV_DUPLICATE_COLUMN";
    public const string TooManyRows = "CSV_TOO_MANY_ROWS";
    public const string ImportNotFound = "IMPORT_NOT_FOUND";
    public const string ImportExpired = "IMPORT_EXPIRED";
    public const string ImportAlreadyCommitted = "IMPORT_ALREADY_COMMITTED";
    public const string ImportHasInvalidRows = "IMPORT_HAS_INVALID_ROWS";
}

public static class RowErrorCodes
{
    public const string FieldCountMismatch = "FIELD_COUNT_MISMATCH";
    public const string Required = "REQUIRED";
    public const string TooLong = "TOO_LONG";
    public const string InvalidEmail = "INVALID_EMAIL";
    public const string InvalidPhone = "INVALID_PHONE";
}
