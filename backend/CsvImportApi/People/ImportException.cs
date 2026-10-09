namespace CsvImportApi.People;

/// <summary>A request-level failure that maps to an HTTP status and an <see cref="ApiErrorBody"/>.</summary>
public sealed class ImportException(int statusCode, string code, string message, IReadOnlyList<object>? details = null)
    : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
    public IReadOnlyList<object>? Details { get; } = details;
}
