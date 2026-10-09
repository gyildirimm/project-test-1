using ExpenseApi.Data;
using ExpenseApi.Endpoints;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Override with ConnectionStrings__Default (env var) or appsettings.json.
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Data Source=expenses.db";

builder.Services.AddDbContext<ExpenseDbContext>(options => options.UseSqlite(connectionString));

var app = builder.Build();

// Creates the SQLite file and schema if missing. Existing databases are left untouched.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<ExpenseDbContext>().Database.EnsureCreated();
}

app.MapExpenseEndpoints();

app.Run();

// Exposed for WebApplicationFactory in the test project.
public partial class Program;
