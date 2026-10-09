using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExpenseApi.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ExpenseApi.Tests;

// xUnit creates a new instance per test, so each test gets its own database file.
public sealed class ExpenseApiTests : IDisposable
{
    private readonly ExpenseApiFactory _factory = new();
    private readonly HttpClient _client;

    public ExpenseApiTests()
    {
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private static object ValidBody(string title = "Taxi") =>
        new { title, description = "Airport run", amount = 42.5, date = "2026-01-15" };

    private async Task<JsonElement> CreateDraftAsync(string title = "Taxi")
    {
        var response = await _client.PostAsJsonAsync("/api/expenses", ValidBody(title));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadJson(response);
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static string IdOf(JsonElement expense) => expense.GetProperty("id").GetString()!;

    private async Task SetStatusAsync(string id, ExpenseStatus status)
    {
        using var db = _factory.CreateDbContext();
        var expense = await db.Expenses.SingleAsync(e => e.Id == Guid.Parse(id));
        db.Entry(expense).Property("Status").CurrentValue = status;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Create_returns_201_with_contract_shape()
    {
        var response = await _client.PostAsJsonAsync("/api/expenses", ValidBody());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var body = await ReadJson(response);
        Assert.Equal("Taxi", body.GetProperty("title").GetString());
        Assert.Equal("Airport run", body.GetProperty("description").GetString());
        Assert.Equal(42.5m, body.GetProperty("amount").GetDecimal());
        Assert.Equal("2026-01-15", body.GetProperty("date").GetString());
        Assert.Equal("Draft", body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("decisionReason").ValueKind);
        Assert.True(body.TryGetProperty("createdAt", out _));
        Assert.True(body.TryGetProperty("updatedAt", out _));
    }

    [Fact]
    public async Task Create_with_invalid_body_returns_400_with_field_errors()
    {
        var response = await _client.PostAsJsonAsync("/api/expenses",
            new { description = "no title", amount = 0, date = "not-a-date" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await ReadJson(response)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("Title", out _));
        Assert.True(errors.TryGetProperty("Amount", out _));
        Assert.True(errors.TryGetProperty("Date", out _));
    }

    [Fact]
    public async Task Create_records_creation_in_history()
    {
        var created = await CreateDraftAsync();

        var detail = await ReadJson(await _client.GetAsync($"/api/expenses/{IdOf(created)}"));
        var history = detail.GetProperty("history");
        Assert.Equal(1, history.GetArrayLength());
        Assert.Equal(JsonValueKind.Null, history[0].GetProperty("fromStatus").ValueKind);
        Assert.Equal("Draft", history[0].GetProperty("toStatus").GetString());
    }

    [Fact]
    public async Task List_returns_drafts_and_submitted_and_supports_status_filter()
    {
        var draft = await CreateDraftAsync("draft");
        var toSubmit = await CreateDraftAsync("submitted");
        await _client.PostAsync($"/api/expenses/{IdOf(toSubmit)}/submit", null);

        var all = await ReadJson(await _client.GetAsync("/api/expenses"));
        Assert.Equal(2, all.GetArrayLength());

        var submitted = await ReadJson(await _client.GetAsync("/api/expenses?status=Submitted"));
        Assert.Equal(1, submitted.GetArrayLength());
        Assert.Equal(IdOf(toSubmit), submitted[0].GetProperty("id").GetString());

        var drafts = await ReadJson(await _client.GetAsync("/api/expenses?status=Draft"));
        Assert.Equal(IdOf(draft), drafts[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task List_with_unknown_status_returns_400()
    {
        var response = await _client.GetAsync("/api/expenses?status=Paid");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadJson(response)).GetProperty("errors").TryGetProperty("Status", out _));
    }

    [Fact]
    public async Task Get_unknown_id_returns_404()
    {
        var response = await _client.GetAsync($"/api/expenses/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_updates_draft()
    {
        var created = await CreateDraftAsync();

        var response = await _client.PutAsJsonAsync($"/api/expenses/{IdOf(created)}",
            new { title = "Train", description = (string?)null, amount = 99, date = "2026-02-01" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJson(response);
        Assert.Equal("Train", body.GetProperty("title").GetString());
        Assert.Equal(99m, body.GetProperty("amount").GetDecimal());
        Assert.Equal("2026-02-01", body.GetProperty("date").GetString());
    }

    [Fact]
    public async Task Put_on_submitted_returns_409()
    {
        var created = await CreateDraftAsync();
        await _client.PostAsync($"/api/expenses/{IdOf(created)}/submit", null);

        var response = await _client.PutAsJsonAsync($"/api/expenses/{IdOf(created)}", ValidBody("changed"));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData(ExpenseStatus.Approved)]
    [InlineData(ExpenseStatus.Rejected)]
    public async Task Put_and_delete_on_decided_expense_return_409(ExpenseStatus status)
    {
        var created = await CreateDraftAsync();
        await SetStatusAsync(IdOf(created), status);

        var put = await _client.PutAsJsonAsync($"/api/expenses/{IdOf(created)}", ValidBody("changed"));
        var delete = await _client.DeleteAsync($"/api/expenses/{IdOf(created)}");

        Assert.Equal(HttpStatusCode.Conflict, put.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
    }

    [Fact]
    public async Task Put_with_invalid_body_on_draft_returns_400()
    {
        var created = await CreateDraftAsync();
        var response = await _client.PutAsJsonAsync($"/api/expenses/{IdOf(created)}", new { title = "", amount = -1, date = "2026-01-01" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Delete_draft_returns_204_and_removes_history()
    {
        var created = await CreateDraftAsync();
        var id = Guid.Parse(IdOf(created));

        var response = await _client.DeleteAsync($"/api/expenses/{id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/expenses/{id}")).StatusCode);
        using var db = _factory.CreateDbContext();
        Assert.False(await db.StatusHistory.AnyAsync(h => h.ExpenseId == id));
    }

    [Fact]
    public async Task Delete_submitted_returns_409()
    {
        var created = await CreateDraftAsync();
        await _client.PostAsync($"/api/expenses/{IdOf(created)}/submit", null);

        var response = await _client.DeleteAsync($"/api/expenses/{IdOf(created)}");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Submit_moves_draft_to_submitted_and_records_history()
    {
        var created = await CreateDraftAsync();

        var response = await _client.PostAsync($"/api/expenses/{IdOf(created)}/submit", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJson(response);
        Assert.Equal("Submitted", body.GetProperty("status").GetString());

        var history = body.GetProperty("history");
        Assert.Equal(2, history.GetArrayLength());
        Assert.Equal("Draft", history[1].GetProperty("fromStatus").GetString());
        Assert.Equal("Submitted", history[1].GetProperty("toStatus").GetString());
    }

    [Fact]
    public async Task Second_submit_returns_409_and_adds_no_history()
    {
        var created = await CreateDraftAsync();
        await _client.PostAsync($"/api/expenses/{IdOf(created)}/submit", null);

        var response = await _client.PostAsync($"/api/expenses/{IdOf(created)}/submit", null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var db = _factory.CreateDbContext();
        var count = await db.StatusHistory.CountAsync(h => h.ExpenseId == Guid.Parse(IdOf(created)));
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task Submit_unknown_id_returns_404()
    {
        var response = await _client.PostAsync($"/api/expenses/{Guid.NewGuid()}/submit", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Data_survives_reopening_the_database()
    {
        var created = await CreateDraftAsync("persisted");
        await _client.PostAsync($"/api/expenses/{IdOf(created)}/submit", null);
        var dbPath = _factory.DbPath;

        // Simulate a restart: new host, same SQLite file.
        _factory.DeleteDatabaseOnDispose = false;
        _client.Dispose();
        _factory.Dispose();
        using var reopened = new ExpenseApiFactory(dbPath);
        using var client = reopened.CreateClient();

        var detail = await ReadJson(await client.GetAsync($"/api/expenses/{IdOf(created)}"));
        Assert.Equal("persisted", detail.GetProperty("title").GetString());
        Assert.Equal("Submitted", detail.GetProperty("status").GetString());
        Assert.Equal(2, detail.GetProperty("history").GetArrayLength());
    }
}
