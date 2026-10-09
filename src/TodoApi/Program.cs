using Microsoft.EntityFrameworkCore;
using TodoApi.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<TodoDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=todos.db"));
builder.Services.AddProblemDetails();

var app = builder.Build();

// Apply pending migrations on startup so the SQLite file is always up to date.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<TodoDbContext>().Database.Migrate();
}

var todos = app.MapGroup("/todos");

todos.MapGet("/", async (TodoDbContext db) =>
    await db.Todos.AsNoTracking()
        .OrderBy(t => t.Id)
        .Select(t => new TodoResponse(t.Id, t.Title, t.IsCompleted, t.CreatedAt))
        .ToListAsync());

todos.MapGet("/{id:int}", async (int id, TodoDbContext db) =>
    await db.Todos.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id) is { } todo
        ? Results.Ok(TodoResponse.From(todo))
        : Results.NotFound());

todos.MapPost("/", async (CreateTodoRequest request, TodoDbContext db) =>
{
    if (Validate(request.Title) is { } errors)
        return Results.ValidationProblem(errors);

    var todo = new Todo
    {
        Title = request.Title!.Trim(),
        IsCompleted = request.IsCompleted,
        CreatedAt = DateTime.UtcNow
    };
    db.Todos.Add(todo);
    await db.SaveChangesAsync();

    return Results.Created($"/todos/{todo.Id}", TodoResponse.From(todo));
});

todos.MapPut("/{id:int}", async (int id, UpdateTodoRequest request, TodoDbContext db) =>
{
    if (Validate(request.Title) is { } errors)
        return Results.ValidationProblem(errors);

    var todo = await db.Todos.FindAsync(id);
    if (todo is null)
        return Results.NotFound();

    todo.Title = request.Title!.Trim();
    todo.IsCompleted = request.IsCompleted;
    await db.SaveChangesAsync();

    return Results.Ok(TodoResponse.From(todo));
});

todos.MapDelete("/{id:int}", async (int id, TodoDbContext db) =>
    await db.Todos.Where(t => t.Id == id).ExecuteDeleteAsync() > 0
        ? Results.NoContent()
        : Results.NotFound());

app.Run();

static Dictionary<string, string[]>? Validate(string? title)
{
    if (string.IsNullOrWhiteSpace(title))
        return new() { ["title"] = ["Title is required."] };
    if (title.Trim().Length > 200)
        return new() { ["title"] = ["Title must be at most 200 characters."] };
    return null;
}

public record CreateTodoRequest(string? Title, bool IsCompleted = false);

public record UpdateTodoRequest(string? Title, bool IsCompleted);

public record TodoResponse(int Id, string Title, bool IsCompleted, DateTime CreatedAt)
{
    public static TodoResponse From(Todo t) => new(t.Id, t.Title, t.IsCompleted, t.CreatedAt);
}

public partial class Program;
