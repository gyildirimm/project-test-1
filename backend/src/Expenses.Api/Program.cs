using Expenses.Api.Data;
using Expenses.Api.Endpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ExpensesDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));

var app = builder.Build();

// Schema is managed by EF Core migrations; applying them on startup keeps data across restarts.
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<ExpensesDbContext>().Database.MigrateAsync();
}

// Serve the frontend/ folder from the same origin. Override with Frontend:Path if needed.
var frontendPath = builder.Configuration["Frontend:Path"]
    ?? Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "..", "..", "frontend"));
if (Directory.Exists(frontendPath))
{
    var frontendFiles = new PhysicalFileProvider(frontendPath);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = frontendFiles });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = frontendFiles });
}

app.MapExpenseEndpoints();
app.MapManagerEndpoints();

app.Run();

// Exposed for WebApplicationFactory in tests.
public partial class Program;
