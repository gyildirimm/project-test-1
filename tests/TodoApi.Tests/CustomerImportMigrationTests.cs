using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TodoApi.Data;

namespace TodoApi.Tests;

public class CustomerImportMigrationTests : IDisposable
{
    private const string InitialMigration = "20261009120935_InitialCreate";

    private readonly TempDb _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task ExistingCustomersDatabase_KeepsCustomers_AndGainsImportTables()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_db.Path)!);
        await CreateDatabaseAtInitialSchemaAsync();

        using var factory = new CustomerApiFactory(_db.Path);
        using var client = factory.CreateClient();

        // The app applies the pending migration on startup; the old customer must still be there.
        var customers = await client.GetFromJsonAsync<JsonElement>("/api/customers");
        var customer = Assert.Single(customers.EnumerateArray());
        Assert.Equal("Ada", customer.GetProperty("name").GetString());
        Assert.Equal("ada@example.com", customer.GetProperty("email").GetString());

        // The import feature works on the migrated file, and its history is separate from customers.
        var csv = new MultipartFormDataContent();
        csv.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("name,email\nGrace,grace@example.com\n")), "file", "new.csv");
        var created = await client.PostAsync("/api/customer-imports", csv);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var history = await client.GetFromJsonAsync<JsonElement>("/api/customer-imports");
        Assert.Single(history.EnumerateArray());

        var after = await client.GetFromJsonAsync<JsonElement>("/api/customers");
        Assert.Equal(2, after.GetArrayLength());
    }

    // Recreates the file as the previous release left it: only InitialCreate applied, with one customer.
    private async Task CreateDatabaseAtInitialSchemaAsync()
    {
        var options = new DbContextOptionsBuilder<CustomerDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = _db.Path }.ToString())
            .Options;

        await using var db = new CustomerDbContext(options);
        await db.Database.GetService<IMigrator>().MigrateAsync(InitialMigration);

        var applied = await db.Database.GetAppliedMigrationsAsync();
        Assert.Equal([InitialMigration], applied);

        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        db.Customers.Add(new Customer { Name = "Ada", Email = "ada@example.com", CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();
    }
}
