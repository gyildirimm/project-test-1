using Microsoft.EntityFrameworkCore;
using TodoApi.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<TodoDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<TodoDbContext>().Database.EnsureCreated();
}

app.UseCors();

var todos = app.MapGroup("/api/todos");

todos.MapGet("/", async (TodoDbContext db) =>
    await db.Todos.AsNoTracking().OrderBy(t => t.Id).ToListAsync());

todos.MapGet("/{id:int}", async (int id, TodoDbContext db) =>
    await db.Todos.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id) is { } todo
        ? Results.Ok(todo)
        : Results.NotFound());

todos.MapPost("/", async (CreateTodoRequest request, TodoDbContext db) =>
{
    if (ValidateTitle(request.Title) is { } error)
        return error;

    var todo = new Todo
    {
        Title = request.Title!.Trim(),
        IsCompleted = false,
        CreatedAt = DateTime.UtcNow,
    };
    db.Todos.Add(todo);
    await db.SaveChangesAsync();

    return Results.Created($"/api/todos/{todo.Id}", todo);
});

todos.MapPut("/{id:int}", async (int id, UpdateTodoRequest request, TodoDbContext db) =>
{
    var todo = await db.Todos.FindAsync(id);
    if (todo is null)
        return Results.NotFound();

    if (ValidateTitle(request.Title) is { } error)
        return error;

    todo.Title = request.Title!.Trim();
    todo.IsCompleted = request.IsCompleted;
    await db.SaveChangesAsync();

    return Results.Ok(todo);
});

todos.MapDelete("/{id:int}", async (int id, TodoDbContext db) =>
{
    var deleted = await db.Todos.Where(t => t.Id == id).ExecuteDeleteAsync();
    return deleted == 0 ? Results.NotFound() : Results.NoContent();
});

app.Run();

static IResult? ValidateTitle(string? title)
{
    if (string.IsNullOrWhiteSpace(title))
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["title"] = ["Title is required."],
        });

    if (title.Trim().Length > Todo.TitleMaxLength)
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["title"] = [$"Title must be at most {Todo.TitleMaxLength} characters."],
        });

    return null;
}

record CreateTodoRequest(string? Title);

record UpdateTodoRequest(string? Title, bool IsCompleted);
