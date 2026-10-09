using CsvImportApi.Csv;
using Xunit;

namespace CsvImportApi.Tests;

public class CsvParserTests
{
    [Fact]
    public void ParsesSimpleRowsWithLfAndCrlf()
    {
        var records = CsvParser.Parse("a,b\r\nc,d\ne,f");

        Assert.Equal(3, records.Count);
        Assert.Equal(new[] { "a", "b" }, records[0].Fields);
        Assert.Equal(2, records[1].LineNumber);
        Assert.Equal(new[] { "e", "f" }, records[2].Fields);
    }

    [Fact]
    public void QuotedFieldsKeepCommasNewlinesAndEscapedQuotes()
    {
        var records = CsvParser.Parse("\"x, y\",\"say \"\"hi\"\"\nline\",z\n");

        Assert.Single(records.Where(r => r.Fields.Count == 3));
        Assert.Equal(new[] { "x, y", "say \"hi\"\nline", "z" }, records[0].Fields);
    }

    [Fact]
    public void LineNumberOfRecordAfterMultilineFieldIsPhysicalLine()
    {
        var records = CsvParser.Parse("\"a\nb\"\nc");

        Assert.Equal(1, records[0].LineNumber);
        Assert.Equal(3, records[1].LineNumber);
    }

    [Fact]
    public void UnclosedQuoteThrowsWithStartLine()
    {
        var ex = Assert.Throws<CsvParseException>(() => CsvParser.Parse("a,b\n\"c,d\n"));

        Assert.Equal(2, ex.LineNumber);
    }
}
