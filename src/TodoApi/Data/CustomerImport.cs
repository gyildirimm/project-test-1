using System.Text.Json.Serialization;

namespace TodoApi.Data;

// Imported is only ever stored on history rows; previews report Importable instead.
[JsonConverter(typeof(JsonStringEnumConverter<ImportRowStatus>))]
public enum ImportRowStatus
{
    Importable,
    Invalid,
    DuplicateInFile,
    AlreadyExists,
    Imported,
}

// One CSV import run. The file content itself is not stored.
public class CustomerImport
{
    public int Id { get; set; }
    public required string FileName { get; set; }
    public DateTime CreatedAt { get; set; }
    public int TotalRows { get; set; }
    public int ImportedCount { get; set; }
    public int InvalidCount { get; set; }
    public int DuplicateInFileCount { get; set; }
    public int AlreadyExistsCount { get; set; }
    public List<CustomerImportRow> Rows { get; set; } = [];
}

public class CustomerImportRow
{
    public int Id { get; set; }
    public int CustomerImportId { get; set; }
    public int LineNumber { get; set; }
    public string? RawName { get; set; }
    public string? RawEmail { get; set; }
    public string? RawPhone { get; set; }
    public ImportRowStatus Status { get; set; }
    public string? Reason { get; set; }

    // Plain column, deliberately not a foreign key: deleting a customer must keep working
    // and the history line keeps pointing at the id it created.
    public int? CustomerId { get; set; }
    public int? DuplicateOfLineNumber { get; set; }
}
