using TodoApi.CustomerImports;
using TodoApi.Data;

namespace TodoApi.Tests;

public class CustomerImportClassifierTests
{
    private static CsvRowInput Row(int line, string? name, string? email = null, string? phone = null) =>
        new(line, name, email, phone);

    private static List<ClassifiedRow> Classify(params CsvRowInput[] rows) =>
        CustomerImportClassifier.Classify(rows, new HashSet<string>());

    [Fact]
    public void ValidRow_IsImportable_WithNormalizedValues()
    {
        var result = Assert.Single(Classify(Row(2, "  Ada  ", " ADA@Example.com ", "  123  ")));

        Assert.Equal(ImportRowStatus.Importable, result.Status);
        Assert.Null(result.Reason);
        Assert.Equal("Ada", result.Valid!.Name);
        Assert.Equal("ADA@Example.com", result.Valid.Email);
        Assert.Equal("123", result.Valid.Phone);
    }

    [Fact]
    public void MissingOrBlankName_IsInvalid()
    {
        var results = Classify(Row(2, null), Row(3, "   "));

        Assert.All(results, r =>
        {
            Assert.Equal(ImportRowStatus.Invalid, r.Status);
            Assert.Equal("Name is required.", r.Reason);
        });
    }

    [Fact]
    public void BadEmail_IsInvalid_BlankEmailIsNotAnError()
    {
        var results = Classify(Row(2, "Ada", "not-an-email"), Row(3, "Grace", "   "));

        Assert.Equal(ImportRowStatus.Invalid, results[0].Status);
        Assert.Equal("Email must be a valid email address.", results[0].Reason);
        Assert.Equal(ImportRowStatus.Importable, results[1].Status);
        Assert.Null(results[1].Valid!.Email);
    }

    [Fact]
    public void NameAndEmailProblems_ReportedTogether()
    {
        var result = Assert.Single(Classify(Row(2, " ", "bad")));

        Assert.Equal(ImportRowStatus.Invalid, result.Status);
        Assert.Contains("Name is required.", result.Reason);
        Assert.Contains("Email must be a valid email address.", result.Reason);
    }

    [Fact]
    public void InvalidRows_DoNotCountAsEarlierOccurrences()
    {
        var results = Classify(Row(2, "Ada", "ada@example.com"), Row(3, "", "ada@example.com"),
            Row(4, "Ada2", "ADA@example.com"));

        Assert.Equal(ImportRowStatus.Invalid, results[1].Status);
        Assert.Equal(ImportRowStatus.DuplicateInFile, results[2].Status);
        Assert.Equal(2, results[2].DuplicateOfLineNumber);
    }

    [Fact]
    public void DuplicateInFile_ByEmail_IsCaseAndWhitespaceInsensitive_PointsAtFirstValidLine()
    {
        var results = Classify(
            Row(2, "Ada", "ada@example.com"),
            Row(3, "Ada Again", "  Ada@Example.COM "),
            Row(4, "Ada Third", "ada@example.com"));

        Assert.Equal(ImportRowStatus.Importable, results[0].Status);
        Assert.Equal(ImportRowStatus.DuplicateInFile, results[1].Status);
        Assert.Equal(2, results[1].DuplicateOfLineNumber);
        Assert.Equal(ImportRowStatus.DuplicateInFile, results[2].Status);
        Assert.Equal(2, results[2].DuplicateOfLineNumber);
    }

    [Fact]
    public void DuplicateKey_WithoutEmail_IsNameAndPhone_CaseInsensitive()
    {
        var results = Classify(
            Row(2, "Ada Lovelace", null, "555"),
            Row(3, "ADA LOVELACE", "", " 555 "),
            Row(4, "Ada Lovelace", null, "556"),
            Row(5, "Ada Lovelace", null, null));

        Assert.Equal(ImportRowStatus.Importable, results[0].Status);
        Assert.Equal(ImportRowStatus.DuplicateInFile, results[1].Status);
        Assert.Equal(2, results[1].DuplicateOfLineNumber);
        Assert.Equal(ImportRowStatus.Importable, results[2].Status);
        Assert.Equal(ImportRowStatus.Importable, results[3].Status);
    }

    [Fact]
    public void Turkish_NamesMatchCaseInsensitively()
    {
        var results = Classify(Row(2, "İlker Öztürk", null, "1"), Row(3, "İLKER ÖZTÜRK", null, "1"));

        Assert.Equal(ImportRowStatus.DuplicateInFile, results[1].Status);
    }

    [Fact]
    public void ExistingCustomerKey_MakesRowAlreadyExists()
    {
        var existing = new HashSet<string>
        {
            CustomerImportClassifier.KeyOf("Ada", "ada@example.com", null),
            CustomerImportClassifier.KeyOf("Grace", null, "555"),
        };

        var results = CustomerImportClassifier.Classify(
            [Row(2, "Someone Else", "ADA@example.com"), Row(3, "grace", null, "555"), Row(4, "Linus", null, "1")],
            existing);

        Assert.Equal(ImportRowStatus.AlreadyExists, results[0].Status);
        Assert.Equal(ImportRowStatus.AlreadyExists, results[1].Status);
        Assert.Equal(ImportRowStatus.Importable, results[2].Status);
    }

    [Fact]
    public void EmailKey_DoesNotCollideWithNamePhoneKey()
    {
        var existing = new HashSet<string> { CustomerImportClassifier.KeyOf("Ada", "ada@example.com", "555") };

        // Same name and phone but no email: a different key, so it is not treated as the existing customer.
        var result = Assert.Single(CustomerImportClassifier.Classify([Row(2, "Ada", null, "555")], existing));

        Assert.Equal(ImportRowStatus.Importable, result.Status);
    }

    [Fact]
    public void AlreadyExists_TakesPrecedenceOverInFileDuplicate()
    {
        var existing = new HashSet<string> { CustomerImportClassifier.KeyOf("Ada", "ada@example.com", null) };

        var results = CustomerImportClassifier.Classify(
            [Row(2, "Ada", "ada@example.com"), Row(3, "Ada", "ada@example.com")], existing);

        Assert.Equal([ImportRowStatus.AlreadyExists, ImportRowStatus.AlreadyExists], results.Select(r => r.Status));
    }
}
