using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TodoApi.Data;

var builder = WebApplication.CreateBuilder(args);

// CUSTOMERS_DB_PATH selects the SQLite file; relative paths resolve from the working directory.
var dbPath = Environment.GetEnvironmentVariable("CUSTOMERS_DB_PATH");
if (string.IsNullOrWhiteSpace(dbPath))
    dbPath = Path.Combine(builder.Environment.ContentRootPath, "customers.db");
dbPath = Path.GetFullPath(dbPath);
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

builder.Services.AddDbContext<CustomerDbContext>(options =>
    options.UseSqlite(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString()));

var app = builder.Build();

// Apply pending migrations on startup so the SQLite file is always up to date.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<CustomerDbContext>().Database.Migrate();
}

app.Run();

public partial class Program;
