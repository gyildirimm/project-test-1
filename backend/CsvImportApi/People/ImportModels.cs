namespace CsvImportApi.People;

public sealed record ImportRowValues(string? FirstName, string? LastName, string? Email, string? Phone);

public sealed record RowErrorDto(string Code, string? Field, string Message);

/// <summary>One CSV data row. <see cref="Line"/> is the physical line in the uploaded file (header is line 1).</summary>
public sealed record ImportRowDto(int Line, string Status, ImportRowValues Values, IReadOnlyList<RowErrorDto> Errors);

public sealed record ImportSummaryDto(int TotalRows, int Valid, int Invalid, int DuplicateInFile, int AlreadyExists);

public sealed record ImportPreviewDto(Guid ImportId, DateTimeOffset ExpiresAt, ImportSummaryDto Summary, IReadOnlyList<ImportRowDto> Rows);

public sealed record ImportCommitSkippedDto(int Invalid, int DuplicateInFile, int AlreadyExists);

public sealed record ImportCommitDto(Guid ImportId, int Imported, ImportCommitSkippedDto Skipped, IReadOnlyList<int> CreatedIds);

public sealed record PersonDto(int Id, string FirstName, string LastName, string Email, string? Phone);

public sealed record ApiErrorDto(string Code, string Message, IReadOnlyList<object>? Details = null);

public sealed record ApiErrorBody(ApiErrorDto Error);
