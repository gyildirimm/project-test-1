using CsvImportApi.People;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

// Allow a little multipart overhead above the file limit so our own JSON error is returned for oversize files.
builder.Services.Configure<FormOptions>(options =>
    options.MultipartBodyLengthLimit = PersonCsvContract.MaxFileBytes + 64 * 1024);

builder.Services.AddSingleton<IPersonRepository, InMemoryPersonRepository>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PersonImportService>();

var app = builder.Build();

app.UseCors();

app.Use(async (context, next) =>
{
    try
    {
        await next(context);
    }
    catch (ImportException ex)
    {
        context.Response.StatusCode = ex.StatusCode;
        await context.Response.WriteAsJsonAsync(
            new ApiErrorBody(new ApiErrorDto(ex.Code, ex.Message, ex.Details)), context.RequestAborted);
    }
});

app.MapPost("/api/people/imports/preview", async (HttpRequest request, PersonImportService imports) =>
{
    if (!request.HasFormContentType)
    {
        throw new ImportException(400, ImportErrorCodes.FileMissing,
            $"Send multipart/form-data with a '{PersonCsvContract.FormFieldName}' file field.");
    }

    var form = await request.ReadFormAsync(request.HttpContext.RequestAborted);
    var file = form.Files.GetFile(PersonCsvContract.FormFieldName);
    if (file is null)
    {
        throw new ImportException(400, ImportErrorCodes.FileMissing,
            $"Missing '{PersonCsvContract.FormFieldName}' file field.");
    }

    await using var stream = file.OpenReadStream();
    var preview = imports.Preview(stream, file.Length);
    return Results.Ok(preview);
});

app.MapPost("/api/people/imports/{importId:guid}/commit", (Guid importId, PersonImportService imports) =>
    Results.Ok(imports.Commit(importId)));

app.MapGet("/api/people", (PersonImportService imports) => Results.Ok(imports.GetPeople()));

app.Run();

public partial class Program;
