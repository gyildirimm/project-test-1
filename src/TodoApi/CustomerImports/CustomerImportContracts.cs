using TodoApi.Data;

namespace TodoApi.CustomerImports;

// Preview rows carry CustomerId = null. Imported rows (commit and detail) carry the new customer's id.
public record ImportRowResponse(
    int LineNumber,
    string? Name,
    string? Email,
    string? Phone,
    ImportRowStatus Status,
    string? Reason,
    int? DuplicateOfLineNumber,
    int? CustomerId);

public record ImportPreviewResponse(
    int TotalRows,
    int ImportableCount,
    int InvalidCount,
    int DuplicateInFileCount,
    int AlreadyExistsCount,
    IReadOnlyList<ImportRowResponse> Rows);

public record ImportSummaryResponse(
    int Id,
    string FileName,
    DateTime CreatedAt,
    int TotalRows,
    int ImportedCount,
    int InvalidCount,
    int DuplicateInFileCount,
    int AlreadyExistsCount);

// Same fields as the summary, plus the rows ordered by lineNumber.
public record ImportDetailResponse(
    int Id,
    string FileName,
    DateTime CreatedAt,
    int TotalRows,
    int ImportedCount,
    int InvalidCount,
    int DuplicateInFileCount,
    int AlreadyExistsCount,
    IReadOnlyList<ImportRowResponse> Rows);
