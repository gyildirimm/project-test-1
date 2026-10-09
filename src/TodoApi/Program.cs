using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TodoApi.Customers;
using TodoApi.Data;

var builder = WebApplication.CreateBuilder(args);

// CUSTOMERS_DB_PATH selects the SQLite file; relative paths resolve from the working directory.
var dbPath = builder.Configuration["CUSTOMERS_DB_PATH"];
if (string.IsNullOrWhiteSpace(dbPath))
    dbPath = Path.Combine(builder.Environment.ContentRootPath, "customers.db");
dbPath = Path.GetFullPath(dbPath);
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

builder.Services.AddDbContext<CustomerDbContext>(options =>
    options.UseSqlite(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString()));

// The browser UI is served from a different port in development, so allow localhost
// origins only. Any other site must not be able to read or change customer data.
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .SetIsOriginAllowed(IsLocalOrigin)
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

app.UseCors();

// Unhandled exceptions get a generic 500 body; details go to the server log only.
app.UseExceptionHandler(errors => errors.Run(async context =>
{
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new ErrorResponse("An unexpected error occurred."));
}));

// Apply pending migrations on startup so the SQLite file is always up to date.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<CustomerDbContext>().Database.Migrate();
}

app.MapCustomerEndpoints();

app.Run();

static bool IsLocalOrigin(string origin) =>
    Uri.TryCreate(origin, UriKind.Absolute, out var uri)
    && (uri.Host == "localhost" || uri.Host == "127.0.0.1");

public partial class Program;
