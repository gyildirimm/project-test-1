using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using TodoApi.Customers;
using TodoApi.Data;

namespace TodoApi.CustomerImports;

public static class CustomerImportEndpoints
{
    public static RouteGroupBuilder MapCustomerImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/customer-imports");

        group.MapPost("/preview", PreviewImport);
        group.MapPost("/", CommitImport);
        group.MapGet("/", ListImports);
        group.MapGet("/{id:int}", GetImport);

        return group;
    }

    // Classifies without writing anything: no customers and no history rows.
    private static async Task<IResult> PreviewImport(HttpRequest http, CustomerDbContext db)
    {
        var (upload, error) = await ReadUpload(http);
        if (error is not null)
            return error;

        var classified = CustomerImportClassifier.Classify(upload!.Rows,
            await CustomerImportClassifier.LoadExistingKeysAsync(db));

        return TypedResults.Ok(new ImportPreviewResponse(
            TotalRows: classified.Count,
            ImportableCount: CountOf(classified, ImportRowStatus.Importable),
            InvalidCount: CountOf(classified, ImportRowStatus.Invalid),
            DuplicateInFileCount: CountOf(classified, ImportRowStatus.DuplicateInFile),
            AlreadyExistsCount: CountOf(classified, ImportRowStatus.AlreadyExists),
            Rows: classified.Select(ToPreviewRow).ToList()));
    }

    // Re-classifies against the database inside the transaction, so the duplicate check sees the state being written.
    // Every row is recorded in history, including when nothing was imported.
    private static async Task<IResult> CommitImport(HttpRequest http, CustomerDbContext db)
    {
        var (upload, error) = await ReadUpload(http);
        if (error is not null)
            return error;

        await using var tx = await db.Database.BeginTransactionAsync();
        var classified = CustomerImportClassifier.Classify(upload!.Rows,
            await CustomerImportClassifier.LoadExistingKeysAsync(db));

        var now = DateTime.UtcNow;
        var insertedByLine = new Dictionary<int, Customer>();
        foreach (var row in classified.Where(r => r.Status == ImportRowStatus.Importable))
        {
            var customer = new Customer
            {
                Name = row.Valid!.Name,
                Email = row.Valid.Email,
                Phone = row.Valid.Phone,
                CreatedAt = now,
                UpdatedAt = now,
            };
            insertedByLine[row.Input.LineNumber] = customer;
            db.Customers.Add(customer);
        }
        // Saved first so each inserted customer has its id for the history row.
        await db.SaveChangesAsync();

        var import = new CustomerImport
        {
            FileName = upload.FileName,
            CreatedAt = now,
            TotalRows = classified.Count,
            ImportedCount = insertedByLine.Count,
            InvalidCount = CountOf(classified, ImportRowStatus.Invalid),
            DuplicateInFileCount = CountOf(classified, ImportRowStatus.DuplicateInFile),
            AlreadyExistsCount = CountOf(classified, ImportRowStatus.AlreadyExists),
        };
        foreach (var row in classified)
        {
            var inserted = insertedByLine.TryGetValue(row.Input.LineNumber, out var customer);
            import.Rows.Add(new CustomerImportRow
            {
                LineNumber = row.Input.LineNumber,
                RawName = row.Input.Name,
                RawEmail = row.Input.Email,
                RawPhone = row.Input.Phone,
                Status = inserted ? ImportRowStatus.Imported : row.Status,
                Reason = row.Reason,
                CustomerId = inserted ? customer!.Id : null,
                DuplicateOfLineNumber = row.DuplicateOfLineNumber,
            });
        }

        db.CustomerImports.Add(import);
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        return TypedResults.Created($"/api/customer-imports/{import.Id}", ToDetail(import, import.Rows));
    }

    private static async Task<Ok<List<ImportSummaryResponse>>> ListImports(CustomerDbContext db) =>
        TypedResults.Ok(await db.CustomerImports.AsNoTracking()
            .OrderByDescending(i => i.CreatedAt)
            .ThenByDescending(i => i.Id)
            .Select(i => new ImportSummaryResponse(
                i.Id, i.FileName, i.CreatedAt, i.TotalRows, i.ImportedCount,
                i.InvalidCount, i.DuplicateInFileCount, i.AlreadyExistsCount))
            .ToListAsync());

    private static async Task<IResult> GetImport(int id, CustomerDbContext db)
    {
        var import = await db.CustomerImports.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id);
        if (import is null)
            return TypedResults.NotFound(new ErrorResponse($"Customer import {id} was not found."));

        var rows = await db.CustomerImportRows.AsNoTracking()
            .Where(r => r.CustomerImportId == id)
            .OrderBy(r => r.LineNumber)
            .ToListAsync();

        return TypedResults.Ok(ToDetail(import, rows));
    }

    // Reads the multipart "file" field. Anything that is not a usable CSV becomes a 400.
    private static async Task<(UploadedCsv? Upload, IResult? Error)> ReadUpload(HttpRequest http)
    {
        if (!http.HasFormContentType)
            return (null, BadRequestWith("Request must be multipart/form-data with a CSV file in the 'file' field."));

        IFormFile? file;
        try
        {
            file = (await http.ReadFormAsync()).Files["file"];
        }
        catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException)
        {
            return (null, BadRequestWith("Request must be multipart/form-data with a CSV file in the 'file' field."));
        }

        if (file is null)
            return (null, BadRequestWith("Upload a CSV file in the 'file' field."));

        try
        {
            await using var stream = file.OpenReadStream();
            var rows = await CustomerCsv.ReadRowsAsync(stream);
            return (new UploadedCsv(Path.GetFileName(file.FileName), rows), null);
        }
        catch (CsvImportException ex)
        {
            return (null, BadRequestWith(ex.Message));
        }
    }

    private static IResult BadRequestWith(string message) =>
        TypedResults.BadRequest(new ErrorResponse(message));

    private static int CountOf(IEnumerable<ClassifiedRow> rows, ImportRowStatus status) =>
        rows.Count(r => r.Status == status);

    private static ImportRowResponse ToPreviewRow(ClassifiedRow row) => new(
        row.Input.LineNumber, row.Input.Name, row.Input.Email, row.Input.Phone,
        row.Status, row.Reason, row.DuplicateOfLineNumber, CustomerId: null);

    private static ImportRowResponse ToRow(CustomerImportRow row) => new(
        row.LineNumber, row.RawName, row.RawEmail, row.RawPhone,
        row.Status, row.Reason, row.DuplicateOfLineNumber, row.CustomerId);

    private static ImportDetailResponse ToDetail(CustomerImport import, IEnumerable<CustomerImportRow> rows) => new(
        import.Id, import.FileName, import.CreatedAt, import.TotalRows, import.ImportedCount,
        import.InvalidCount, import.DuplicateInFileCount, import.AlreadyExistsCount,
        rows.OrderBy(r => r.LineNumber).Select(ToRow).ToList());

    private sealed record UploadedCsv(string FileName, List<CsvRowInput> Rows);
}
