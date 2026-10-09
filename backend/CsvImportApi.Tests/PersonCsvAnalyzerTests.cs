using CsvImportApi.People;
using Xunit;

namespace CsvImportApi.Tests;

public class PersonCsvAnalyzerTests
{
    private const string Header = "first_name,last_name,email,phone\n";

    private static IReadOnlyList<ImportRowDto> Analyze(string body, params string[] existingEmails) =>
        PersonCsvAnalyzer.Analyze(Header + body, key => existingEmails.Contains(key));

    [Fact]
    public void ValidRowIsMarkedValidWithLineNumber()
    {
        var rows = Analyze("Ada,Lovelace,ada@example.com,+90 555 123 45 67\n");

        var row = Assert.Single(rows);
        Assert.Equal(2, row.Line);
        Assert.Equal(ImportRowStatus.Valid, row.Status);
        Assert.Equal("ada@example.com", row.Values.Email);
        Assert.Equal("+90 555 123 45 67", row.Values.Phone);
    }

    [Fact]
    public void PhoneIsOptional()
    {
        var rows = PersonCsvAnalyzer.Analyze("first_name,last_name,email\nAda,Lovelace,ada@example.com\n", _ => false);

        Assert.Equal(ImportRowStatus.Valid, Assert.Single(rows).Status);
        Assert.Null(Assert.Single(rows).Values.Phone);
    }

    [Fact]
    public void HeaderIsCaseInsensitiveAndTrimmedAndColumnOrderFree()
    {
        var rows = PersonCsvAnalyzer.Analyze(" Email , FIRST_NAME,last_name\nada@example.com,Ada,Lovelace\n", _ => false);

        Assert.Equal(ImportRowStatus.Valid, Assert.Single(rows).Status);
    }

    [Fact]
    public void MissingRequiredFieldsAreRowErrorsNotFileErrors()
    {
        var rows = Analyze(",Lovelace,not-an-email,\n");

        var row = Assert.Single(rows);
        Assert.Equal(ImportRowStatus.Invalid, row.Status);
        Assert.Contains(row.Errors, e => e.Code == RowErrorCodes.Required && e.Field == "first_name");
        Assert.Contains(row.Errors, e => e.Code == RowErrorCodes.InvalidEmail && e.Field == "email");
    }

    [Fact]
    public void InvalidPhoneAndOverlongNameAreRejected()
    {
        var longName = new string('x', PersonCsvContract.MaxNameLength + 1);
        var rows = Analyze($"{longName},Lovelace,ada@example.com,12ab\n");

        var row = Assert.Single(rows);
        Assert.Contains(row.Errors, e => e.Code == RowErrorCodes.TooLong && e.Field == "first_name");
        Assert.Contains(row.Errors, e => e.Code == RowErrorCodes.InvalidPhone && e.Field == "phone");
    }

    [Fact]
    public void EmailDedupeIsCaseAndWhitespaceInsensitiveAndFirstOccurrenceWins()
    {
        var rows = Analyze(
            "Ada,Lovelace,ADA@example.com,\n" +
            "Ada,L,  ada@example.com ,\n" +
            "Bob,Byte,bob@example.com,\n");

        Assert.Equal(new[] { ImportRowStatus.Valid, ImportRowStatus.DuplicateInFile, ImportRowStatus.Valid },
            rows.Select(r => r.Status).ToArray());
        Assert.Equal(3, rows[1].Line);
    }

    [Fact]
    public void EmailAlreadyStoredIsAlreadyExists()
    {
        var rows = Analyze("Ada,Lovelace,ada@example.com,\n", "ada@example.com");

        Assert.Equal(ImportRowStatus.AlreadyExists, Assert.Single(rows).Status);
    }

    [Fact]
    public void InvalidRowDoesNotReserveItsEmailForDedupe()
    {
        var rows = Analyze(
            ",Lovelace,ada@example.com,\n" +
            "Ada,Lovelace,ada@example.com,\n");

        Assert.Equal(ImportRowStatus.Invalid, rows[0].Status);
        Assert.Equal(ImportRowStatus.Valid, rows[1].Status);
    }

    [Fact]
    public void BlankLinesAreSkippedAndNotCounted()
    {
        var rows = Analyze("\nAda,Lovelace,ada@example.com,\n\n,,,\n");

        // ",,," is all-empty and therefore treated as a blank line.
        Assert.Single(rows);
    }

    [Fact]
    public void FieldCountMismatchIsRowError()
    {
        var rows = Analyze("Ada,Lovelace,ada@example.com,,extra\n");

        Assert.Contains(Assert.Single(rows).Errors, e => e.Code == RowErrorCodes.FieldCountMismatch);
    }

    [Fact]
    public void UnknownHeaderColumnIsFileError()
    {
        var ex = Assert.Throws<ImportException>(() =>
            PersonCsvAnalyzer.Analyze("first_name,last_name,email,company\nA,B,a@b.co,X\n", _ => false));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(ImportErrorCodes.UnknownColumn, ex.Code);
    }

    [Fact]
    public void MissingRequiredHeaderIsFileError()
    {
        var ex = Assert.Throws<ImportException>(() =>
            PersonCsvAnalyzer.Analyze("first_name,email\nA,a@b.co\n", _ => false));

        Assert.Equal(ImportErrorCodes.MissingColumn, ex.Code);
    }

    [Fact]
    public void DuplicateHeaderIsFileError()
    {
        var ex = Assert.Throws<ImportException>(() =>
            PersonCsvAnalyzer.Analyze("first_name,First_Name,last_name,email\n", _ => false));

        Assert.Equal(ImportErrorCodes.DuplicateColumn, ex.Code);
    }

    [Fact]
    public void RowCountAboveLimitIsRejected()
    {
        var body = string.Concat(Enumerable.Range(0, PersonCsvContract.MaxDataRows + 1)
            .Select(i => $"A,B,user{i}@example.com,\n"));

        var ex = Assert.Throws<ImportException>(() => Analyze(body));

        Assert.Equal(422, ex.StatusCode);
        Assert.Equal(ImportErrorCodes.TooManyRows, ex.Code);
    }

    [Fact]
    public void RowCountAtLimitIsAccepted()
    {
        var body = string.Concat(Enumerable.Range(0, PersonCsvContract.MaxDataRows)
            .Select(i => $"A,B,user{i}@example.com,\n"));

        Assert.Equal(PersonCsvContract.MaxDataRows, Analyze(body).Count);
    }

    [Fact]
    public void EmptyFileIsFileError()
    {
        var ex = Assert.Throws<ImportException>(() => PersonCsvAnalyzer.Analyze("\n\n", _ => false));

        Assert.Equal(ImportErrorCodes.EmptyFile, ex.Code);
    }
}
