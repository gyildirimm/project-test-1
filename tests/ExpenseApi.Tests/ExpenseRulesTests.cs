using System.Text.Json;
using ExpenseApi.Endpoints;
using ExpenseApi.Models;
using Xunit;

namespace ExpenseApi.Tests;

public class ExpenseRulesTests
{
    [Fact]
    public void Draft_can_only_be_submitted()
    {
        Assert.True(ExpenseStatusRules.CanTransition(ExpenseStatus.Draft, ExpenseStatus.Submitted));
        Assert.False(ExpenseStatusRules.CanTransition(ExpenseStatus.Draft, ExpenseStatus.Draft));
        Assert.False(ExpenseStatusRules.CanTransition(ExpenseStatus.Draft, ExpenseStatus.Approved));
        Assert.False(ExpenseStatusRules.CanTransition(ExpenseStatus.Draft, ExpenseStatus.Rejected));
    }

    [Theory]
    [InlineData(ExpenseStatus.Approved)]
    [InlineData(ExpenseStatus.Rejected)]
    public void Terminal_statuses_have_no_outgoing_transitions(ExpenseStatus to)
    {
        foreach (var from in new[] { ExpenseStatus.Approved, ExpenseStatus.Rejected })
        {
            Assert.False(ExpenseStatusRules.CanTransition(from, to));
        }
    }

    [Fact]
    public void Only_drafts_are_editable_and_deletable()
    {
        foreach (var status in Enum.GetValues<ExpenseStatus>())
        {
            var expected = status == ExpenseStatus.Draft;
            Assert.Equal(expected, ExpenseStatusRules.IsEditable(status));
            Assert.Equal(expected, ExpenseStatusRules.IsDeletable(status));
        }
    }

    [Fact]
    public void TryTransition_to_submitted_appends_history_and_second_submit_is_rejected()
    {
        var expense = Expense.CreateDraft("Taxi", null, 42m, new DateOnly(2026, 1, 2), DateTimeOffset.UtcNow);

        Assert.True(expense.TryTransition(ExpenseStatus.Submitted, DateTimeOffset.UtcNow, "ready"));
        Assert.Equal(ExpenseStatus.Submitted, expense.Status);
        Assert.False(expense.TryTransition(ExpenseStatus.Submitted, DateTimeOffset.UtcNow));

        Assert.Collection(expense.History,
            creation =>
            {
                Assert.Null(creation.FromStatus);
                Assert.Equal(ExpenseStatus.Draft, creation.ToStatus);
            },
            submit =>
            {
                Assert.Equal(ExpenseStatus.Draft, submit.FromStatus);
                Assert.Equal(ExpenseStatus.Submitted, submit.ToStatus);
                Assert.Equal("ready", submit.Note);
            });
    }
}

public class ExpenseValidatorTests
{
    private static ExpenseValidation Validate(string json) =>
        ExpenseValidator.Validate(JsonDocument.Parse(json).RootElement);

    [Fact]
    public void Valid_body_produces_values()
    {
        var result = Validate("""{"title":"  Lunch ","description":"","amount":12.5,"date":"2026-03-04"}""");

        Assert.True(result.IsValid);
        Assert.Equal("Lunch", result.Values!.Title);
        Assert.Null(result.Values.Description);
        Assert.Equal(12.5m, result.Values.Amount);
        Assert.Equal(new DateOnly(2026, 3, 4), result.Values.Date);
    }

    [Fact]
    public void Missing_title_and_amount_and_date_are_reported_per_field()
    {
        var result = Validate("""{}""");

        Assert.False(result.IsValid);
        Assert.Contains("Title is required.", result.Errors["Title"]);
        Assert.Contains("Amount is required.", result.Errors["Amount"]);
        Assert.Contains("Date is required.", result.Errors["Date"]);
    }

    [Theory]
    [InlineData("""{"title":"x","amount":0,"date":"2026-01-01"}""")]
    [InlineData("""{"title":"x","amount":-5,"date":"2026-01-01"}""")]
    [InlineData("""{"title":"x","amount":"10","date":"2026-01-01"}""")]
    public void Amount_must_be_a_positive_number(string json)
    {
        var result = Validate(json);
        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("Amount"));
    }

    [Theory]
    [InlineData("01/02/2026")]
    [InlineData("2026-02-30")]
    [InlineData("2026-1-5")]
    [InlineData("2026-01-01T00:00:00")]
    public void Date_must_be_yyyy_mm_dd(string date)
    {
        var result = Validate($$"""{"title":"x","amount":1,"date":"{{date}}"}""");
        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("Date"));
    }

    [Fact]
    public void Title_length_limit_is_enforced()
    {
        var title = new string('a', ExpenseValidator.TitleMaxLength + 1);
        var result = Validate($$"""{"title":"{{title}}","amount":1,"date":"2026-01-01"}""");
        Assert.Contains($"Title must be at most {ExpenseValidator.TitleMaxLength} characters.", result.Errors["Title"]);
    }

    [Fact]
    public void Whitespace_only_title_is_missing()
    {
        var result = Validate("""{"title":"   ","amount":1,"date":"2026-01-01"}""");
        Assert.Contains("Title is required.", result.Errors["Title"]);
    }
}
