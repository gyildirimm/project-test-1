using System.Text.Json;
using System.Net;
using System.Net.Http.Json;
using Expenses.Api.Data;
using Expenses.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Expenses.Tests;

public class ExpenseApiTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "expenses-tests-" + Guid.NewGuid().ToString("N"));
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public ExpenseApiTests()
    {
        Directory.CreateDirectory(_dir);
        _factory = new ApiFactory(Path.Combine(_dir, "test.db"));
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        SqliteConnectionCleanup.TryDeleteDirectory(_dir);
    }

    [Fact]
    public async Task Create_valid_draft_returns_201_with_initial_history()
    {
        var response = await _client.PostAsJsonAsync("/api/expenses", Valid());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var body = await response.Content.ReadFromJsonAsync<ExpenseDetail>(Json);
        Assert.NotNull(body);
        Assert.Equal("Draft", body!.Status);
        Assert.Equal("2026-03-15", body.Date.ToString("yyyy-MM-dd"));
        var history = Assert.Single(body.History);
        Assert.Null(history.FromStatus);
        Assert.Equal("Draft", history.ToStatus);
    }

    [Fact]
    public async Task Create_with_invalid_input_returns_400_problem_with_field_errors()
    {
        var response = await _client.PostAsJsonAsync("/api/expenses",
            new { title = "  ", amount = 0, date = "2026-02-30" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Json);
        Assert.NotNull(problem);
        Assert.Contains("title", problem!.Errors.Keys);
        Assert.Contains("amount", problem.Errors.Keys);
        Assert.Contains("date", problem.Errors.Keys);
    }

    [Fact]
    public async Task Create_rejects_overlong_title()
    {
        var response = await _client.PostAsJsonAsync("/api/expenses",
            Valid() with { Title = new string('a', 201) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Draft_can_be_updated_and_deleted()
    {
        var created = await CreateAsync();

        var put = await _client.PutAsJsonAsync($"/api/expenses/{created.Id}",
            Valid() with { Title = "Updated", Amount = 99.5m });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var updated = await put.Content.ReadFromJsonAsync<ExpenseDetail>(Json);
        Assert.Equal("Updated", updated!.Title);
        Assert.Equal(99.5m, updated.Amount);

        var del = await _client.DeleteAsync($"/api/expenses/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/expenses/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task Pending_expense_cannot_be_updated_or_deleted()
    {
        var created = await CreateAsync();
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync($"/api/expenses/{created.Id}/submit", null)).StatusCode);

        var put = await _client.PutAsJsonAsync($"/api/expenses/{created.Id}", Valid());
        var del = await _client.DeleteAsync($"/api/expenses/{created.Id}");

        Assert.Equal(HttpStatusCode.Conflict, put.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, del.StatusCode);
    }

    [Theory]
    [InlineData(ExpenseStatus.Approved)]
    [InlineData(ExpenseStatus.Rejected)]
    public async Task Approved_or_rejected_expense_cannot_be_updated_or_deleted(ExpenseStatus status)
    {
        // No approve/reject endpoints exist yet, so the status is set directly in the database.
        var created = await CreateAsync();
        await SetStatusAsync(created.Id, status);

        var put = await _client.PutAsJsonAsync($"/api/expenses/{created.Id}", Valid());
        var del = await _client.DeleteAsync($"/api/expenses/{created.Id}");

        Assert.Equal(HttpStatusCode.Conflict, put.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, del.StatusCode);
    }

    [Fact]
    public async Task Submit_moves_draft_to_pending_and_records_history()
    {
        var created = await CreateAsync();

        var response = await _client.PostAsync($"/api/expenses/{created.Id}/submit", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ExpenseDetail>(Json);
        Assert.Equal("Pending", body!.Status);
        Assert.Equal(2, body.History.Count);
        Assert.Equal("Draft", body.History[1].FromStatus);
        Assert.Equal("Pending", body.History[1].ToStatus);
    }

    [Fact]
    public async Task Submit_twice_returns_409()
    {
        var created = await CreateAsync();
        await _client.PostAsync($"/api/expenses/{created.Id}/submit", null);

        var second = await _client.PostAsync($"/api/expenses/{created.Id}/submit", null);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Missing_expense_returns_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/expenses/9999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PutAsJsonAsync("/api/expenses/9999", Valid())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync("/api/expenses/9999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsync("/api/expenses/9999/submit", null)).StatusCode);
    }

    [Fact]
    public async Task List_filters_by_status_and_detail_includes_history()
    {
        var draft = await CreateAsync();
        var toSubmit = await CreateAsync();
        await _client.PostAsync($"/api/expenses/{toSubmit.Id}/submit", null);

        var all = await _client.GetFromJsonAsync<List<ExpenseDetail>>("/api/expenses", Json);
        Assert.Equal(2, all!.Count);

        var pending = await _client.GetFromJsonAsync<List<ExpenseDetail>>("/api/expenses?status=Pending", Json);
        var pendingItem = Assert.Single(pending!);
        Assert.Equal(toSubmit.Id, pendingItem.Id);
        Assert.Equal("Pending", pendingItem.Status);

        var drafts = await _client.GetFromJsonAsync<List<ExpenseDetail>>("/api/expenses?status=Draft", Json);
        Assert.Equal(draft.Id, Assert.Single(drafts!).Id);

        var detail = await _client.GetFromJsonAsync<ExpenseDetail>($"/api/expenses/{toSubmit.Id}", Json);
        Assert.Equal(2, detail!.History.Count);
    }

    [Fact]
    public async Task List_with_unknown_status_returns_400()
    {
        var response = await _client.GetAsync("/api/expenses?status=Bogus");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Json);
        Assert.Contains("status", problem!.Errors.Keys);
    }

    [Fact]
    public async Task Data_survives_application_restart()
    {
        var created = await CreateAsync();
        await _client.PostAsync($"/api/expenses/{created.Id}/submit", null);
        _client.Dispose();
        _factory.Dispose();

        using var restarted = new ApiFactory(_factory.DbPath);
        using var client = restarted.CreateClient();
        var detail = await client.GetFromJsonAsync<ExpenseDetail>($"/api/expenses/{created.Id}", Json);

        Assert.Equal("Pending", detail!.Status);
        Assert.Equal(new[] { "Draft", "Pending" }, detail.History.Select(h => h.ToStatus).ToArray());
    }

    private static ExpenseRequestDto Valid() => new("Taxi", "Airport transfer", 42.50m, "2026-03-15");

    private async Task<ExpenseDetail> CreateAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/expenses", Valid());
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ExpenseDetail>(Json))!;
    }

    private async Task SetStatusAsync(int id, ExpenseStatus status)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExpensesDbContext>();
        var expense = await db.Expenses.FindAsync(id);
        expense!.Status = status;
        await db.SaveChangesAsync();
    }
}

public record ExpenseRequestDto(string Title, string? Description, decimal Amount, string Date);

public record ExpenseDetail(
    int Id,
    string Title,
    string? Description,
    decimal Amount,
    DateOnly Date,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    List<StatusHistoryItem> History);

public record StatusHistoryItem(string? FromStatus, string ToStatus, string? Reason, DateTimeOffset OccurredAt);
