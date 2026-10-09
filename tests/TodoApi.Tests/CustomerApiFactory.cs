using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace TodoApi.Tests;

// Runs the real app against a SQLite file the test owns, so each test (or each
// "app lifetime" in the restart test) can point at its own database.
public class CustomerApiFactory(string dbPath) : WebApplicationFactory<Program>
{
    public string DbPath { get; } = dbPath;

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("CUSTOMERS_DB_PATH", DbPath);
    }
}

// Creates a fresh temp database path and removes it afterwards.
public sealed class TempDb : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), $"customers-test-{Guid.NewGuid():N}", "customers.db");

    public void Dispose()
    {
        // Release pooled SQLite handles before deleting the file.
        SqliteConnection.ClearAllPools();
        var dir = System.IO.Path.GetDirectoryName(Path)!;
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }
}
