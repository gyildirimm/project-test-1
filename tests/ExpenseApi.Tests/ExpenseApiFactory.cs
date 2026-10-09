using ExpenseApi.Data;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExpenseApi.Tests;

/// <summary>Runs the API against a throwaway SQLite file. Dispose deletes the file.</summary>
public sealed class ExpenseApiFactory(string dbPath) : WebApplicationFactory<Program>
{
    public ExpenseApiFactory() : this(Path.Combine(Path.GetTempPath(), $"expenses-{Guid.NewGuid():N}.db"))
    {
    }

    public string DbPath { get; } = dbPath;

    /// <summary>Set to false when a later factory must reopen the same file.</summary>
    public bool DeleteDatabaseOnDispose { get; set; } = true;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Replace the options directly: appsettings.json values win over host-level config overrides.
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<ExpenseDbContext>>();
            services.AddDbContext<ExpenseDbContext>(o => o.UseSqlite($"Data Source={DbPath}"));
        });
    }

    public ExpenseDbContext CreateDbContext()
    {
        var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ExpenseDbContext>();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            SqliteConnection.ClearAllPools();
            if (DeleteDatabaseOnDispose)
            {
                TryDelete(DbPath);
            }
        }
    }

    private static void TryDelete(string path)
    {
        foreach (var file in new[] { path, path + "-shm", path + "-wal" })
        {
            try { File.Delete(file); } catch (IOException) { }
        }
    }
}
