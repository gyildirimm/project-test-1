using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Expenses.Tests;

// Runs the real API against a file-based SQLite db so data can be checked across "restarts".
public class ApiFactory(string dbPath) : WebApplicationFactory<Program>
{
    public string DbPath { get; } = dbPath;

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={DbPath}");
}
