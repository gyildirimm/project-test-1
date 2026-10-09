using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Expenses.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Expenses.Tests;

public class ManagerApiTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "expenses-manager-tests-" + Guid.NewGuid().ToString("N"));
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public ManagerApiTests()
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
    public async Task Pending_list_returns_only_pending_expenses()
    {
        var pending = await CreateAsync(amount: 10m);
        await SubmitAsync(pending.Id);
        await CreateAsync(amount: 20m); // stays Draft
        var approved = await CreateAsync(amount: 30m);
        await SubmitAsync(approved.Id);
        await _client.PostAsync($"/api/manager/expenses/{approved.Id}/approve", null);

        var list = await _client.GetFromJsonAsync<List<ExpenseDetail>>("/api/manager/expenses/pending", Json);

        Assert.NotNull(list);
        var item = Assert.Single(list!);
        Assert.Equal(pending.Id, item.Id);
        Assert.Equal("Pending", item.Status);
    }

    [Fact]
    public async Task Pending_list_is_empty_array_when_nothing_is_pending()
    {
        var list = await _client.GetFromJsonAsync<List<ExpenseDetail>>("/api/manager/expenses/pending", Json);

        Assert.NotNull(list);
        Assert.Empty(list!);
    }

    [Fact]
    public async Task Approve_pending_sets_approved_and_records_history()
    {
        var created = await CreateAsync();
        await SubmitAsync(created.Id);

        var response = await _client.PostAsync($"/api/manager/expenses/{created.Id}/approve", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ExpenseDetail>(Json);
        Assert.Equal("Approved", body!.Status);
        var entry = Assert.Single(body.History.Where(h => h.ToStatus == "Approved"));
        Assert.Equal("Pending", entry.FromStatus);
        Assert.Null(entry.Reason);
        Assert.Equal(new[] { "Draft", "Pending", "Approved" }, body.History.Select(h => h.ToStatus).ToArray());
    }

    [Fact]
    public async Task Reject_pending_sets_rejected_and_records_trimmed_reason()
    {
        var created = await CreateAsync();
        await SubmitAsync(created.Id);

        var response = await _client.PostAsJsonAsync($"/api/manager/expenses/{created.Id}/reject", new { reason = "  Missing receipt  " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ExpenseDetail>(Json);
        Assert.Equal("Rejected", body!.Status);
        var entry = body.History.Last();
        Assert.Equal("Pending", entry.FromStatus);
        Assert.Equal("Rejected", entry.ToStatus);
        Assert.Equal("Missing receipt", entry.Reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Reject_without_reason_returns_400_and_keeps_pending(string? reason)
    {
        var created = await CreateAsync();
        await SubmitAsync(created.Id);

        var response = await _client.PostAsJsonAsync($"/api/manager/expenses/{created.Id}/reject", new { reason });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Json);
        Assert.Contains("reason", problem!.Errors.Keys);
        Assert.Equal("Pending", (await GetDetailAsync(created.Id)).Status);
    }

    [Fact]
    public async Task Reject_with_overlong_reason_returns_400()
    {
        var created = await CreateAsync();
        await SubmitAsync(created.Id);

        var response = await _client.PostAsJsonAsync($"/api/manager/expenses/{created.Id}/reject", new { reason = new string('x', 501) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Json);
        Assert.Contains("reason", problem!.Errors.Keys);
    }

    [Fact]
    public async Task Approve_and_reject_draft_returns_409()
    {
        var created = await CreateAsync();

        var approve = await _client.PostAsync($"/api/manager/expenses/{created.Id}/approve", null);
        var reject = await _client.PostAsJsonAsync($"/api/manager/expenses/{created.Id}/reject", new { reason = "no" });

        Assert.Equal(HttpStatusCode.Conflict, approve.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, reject.StatusCode);
        Assert.Equal("Draft", (await GetDetailAsync(created.Id)).Status);
    }

    [Theory]
    [InlineData(ExpenseStatus.Approved)]
    [InlineData(ExpenseStatus.Rejected)]
    public async Task Approve_and_reject_concluded_expense_returns_409(ExpenseStatus status)
    {
        var created = await CreateAsync();
        await SubmitAsync(created.Id);
        await SetStatusAsync(created.Id, status);

        var approve = await _client.PostAsync($"/api/manager/expenses/{created.Id}/approve", null);
        var reject = await _client.PostAsJsonAsync($"/api/manager/expenses/{created.Id}/reject", new { reason = "again" });

        Assert.Equal(HttpStatusCode.Conflict, approve.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, reject.StatusCode);
        Assert.Equal(status.ToString(), (await GetDetailAsync(created.Id)).Status);
    }

    [Fact]
    public async Task Approving_twice_returns_409_and_adds_no_second_history_entry()
    {
        var created = await CreateAsync();
        await SubmitAsync(created.Id);
        await _client.PostAsync($"/api/manager/expenses/{created.Id}/approve", null);

        var second = await _client.PostAsync($"/api/manager/expenses/{created.Id}/approve", null);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(3, (await GetDetailAsync(created.Id)).History.Count);
    }

    [Fact]
    public async Task Missing_expense_returns_404_for_approve_and_reject()
    {
        var approve = await _client.PostAsync("/api/manager/expenses/999/approve", null);
        var reject = await _client.PostAsJsonAsync("/api/manager/expenses/999/reject", new { reason = "x" });

        Assert.Equal(HttpStatusCode.NotFound, approve.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, reject.StatusCode);
    }

    [Fact]
    public async Task Summary_on_empty_database_returns_zero_for_every_status()
    {
        using var doc = JsonDocument.Parse(await _client.GetStringAsync("/api/manager/summary"));
        var root = doc.RootElement;
        var counts = root.GetProperty("counts");

        Assert.Equal(0, counts.GetProperty("Draft").GetInt32());
        Assert.Equal(0, counts.GetProperty("Pending").GetInt32());
        Assert.Equal(0, counts.GetProperty("Approved").GetInt32());
        Assert.Equal(0, counts.GetProperty("Rejected").GetInt32());
        Assert.Equal(0m, root.GetProperty("approvedTotal").GetDecimal());
    }

    [Fact]
    public async Task Summary_counts_every_status_and_totals_only_approved_amounts()
    {
        await CreateAsync(amount: 5m); // Draft
        var pending = await CreateAsync(amount: 7m);
        await SubmitAsync(pending.Id);
        var approvedA = await CreateAsync(amount: 10.25m);
        await SubmitAsync(approvedA.Id);
        await _client.PostAsync($"/api/manager/expenses/{approvedA.Id}/approve", null);
        var approvedB = await CreateAsync(amount: 4.75m);
        await SubmitAsync(approvedB.Id);
        await _client.PostAsync($"/api/manager/expenses/{approvedB.Id}/approve", null);
        var rejected = await CreateAsync(amount: 99m);
        await SubmitAsync(rejected.Id);
        await _client.PostAsJsonAsync($"/api/manager/expenses/{rejected.Id}/reject", new { reason = "no" });

        using var doc = JsonDocument.Parse(await _client.GetStringAsync("/api/manager/summary"));
        var root = doc.RootElement;
        var counts = root.GetProperty("counts");

        Assert.Equal(1, counts.GetProperty("Draft").GetInt32());
        Assert.Equal(1, counts.GetProperty("Pending").GetInt32());
        Assert.Equal(2, counts.GetProperty("Approved").GetInt32());
        Assert.Equal(1, counts.GetProperty("Rejected").GetInt32());
        Assert.Equal(15m, root.GetProperty("approvedTotal").GetDecimal());
    }

    private async Task<ExpenseDetail> CreateAsync(decimal amount = 42.50m)
    {
        var response = await _client.PostAsJsonAsync("/api/expenses",
            new ExpenseRequestDto("Taxi", null, amount, "2026-03-15"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ExpenseDetail>(Json))!;
    }

    private async Task SubmitAsync(int id)
    {
        var response = await _client.PostAsync($"/api/expenses/{id}/submit", null);
        response.EnsureSuccessStatusCode();
    }

    private async Task<ExpenseDetail> GetDetailAsync(int id) =>
        (await _client.GetFromJsonAsync<ExpenseDetail>($"/api/expenses/{id}", Json))!;

    private async Task SetStatusAsync(int id, ExpenseStatus status)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Expenses.Api.Data.ExpensesDbContext>();
        var expense = await db.Expenses.FindAsync(id);
        expense!.Status = status;
        await db.SaveChangesAsync();
    }
}
