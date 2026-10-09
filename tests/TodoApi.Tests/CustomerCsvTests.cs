using System.Text;
using TodoApi.CustomerImports;

namespace TodoApi.Tests;

public class CustomerCsvTests
{
    private static async Task<List<CsvRowInput>> ReadAsync(string csv) =>
        await CustomerCsv.ReadRowsAsync(new MemoryStream(Encoding.UTF8.GetBytes(csv)));

    private static async Task<List<CsvRowInput>> ReadBytesAsync(byte[] bytes) =>
        await CustomerCsv.ReadRowsAsync(new MemoryStream(bytes));

    [Fact]
    public async Task Parses_HeaderCaseInsensitively_AndIgnoresUnknownColumns()
    {
        var rows = await ReadAsync("Notes,NAME,Email,City\r\nx,Ada,ada@example.com,London\r\n");

        var row = Assert.Single(rows);
        Assert.Equal(2, row.LineNumber);
        Assert.Equal("Ada", row.Name);
        Assert.Equal("ada@example.com", row.Email);
        Assert.Null(row.Phone);
    }

    [Fact]
    public async Task StripsUtf8ByteOrderMark()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(Encoding.UTF8.GetBytes("name,email\nAda,ada@example.com\n")).ToArray();

        var row = Assert.Single(await ReadBytesAsync(bytes));

        Assert.Equal("Ada", row.Name);
    }

    [Fact]
    public async Task Header_WithBomAndQuotedName_IsRecognised()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(Encoding.UTF8.GetBytes("\"name\",\"phone\"\n\"Ada\",\"123\"\n")).ToArray();

        var row = Assert.Single(await ReadBytesAsync(bytes));

        Assert.Equal("Ada", row.Name);
        Assert.Equal("123", row.Phone);
    }

    [Fact]
    public async Task QuotedFields_KeepCommasQuotesAndLineBreaks()
    {
        var rows = await ReadAsync("name,phone\n\"Doe, Jane\",\"+1 \"\"555\"\"\"\n\"Multi\nLine\",1\n");

        Assert.Equal(2, rows.Count);
        Assert.Equal("Doe, Jane", rows[0].Name);
        Assert.Equal("+1 \"555\"", rows[0].Phone);
        Assert.Equal("Multi\nLine", rows[1].Name);
        Assert.Equal(2, rows[0].LineNumber);
        Assert.Equal(3, rows[1].LineNumber);
    }

    [Fact]
    public async Task LineNumbers_CountPhysicalLinesIncludingBlankLinesAndMultilineFields()
    {
        var rows = await ReadAsync("name\n\nAda\n\n\"Grace\nHopper\"\nLinus\n");

        Assert.Equal([3, 5, 7], rows.Select(r => r.LineNumber));
    }

    [Fact]
    public async Task PreservesTurkishCharacters()
    {
        var rows = await ReadAsync("name,email\nÇağla Şahin,cagla@örnek.com\nİlker Öztürk,ilker@example.com\n");

        Assert.Equal("Çağla Şahin", rows[0].Name);
        Assert.Equal("cagla@örnek.com", rows[0].Email);
        Assert.Equal("İlker Öztürk", rows[1].Name);
    }

    [Fact]
    public async Task ShortRows_LeaveMissingCellsNull()
    {
        var row = Assert.Single(await ReadAsync("name,email,phone\nAda\n"));

        Assert.Equal("Ada", row.Name);
        Assert.Null(row.Email);
        Assert.Null(row.Phone);
    }

    [Fact]
    public async Task MissingNameColumn_Throws()
    {
        var ex = await Assert.ThrowsAsync<CsvImportException>(() => ReadAsync("email,phone\na@b.co,1\n"));

        Assert.Contains("'name'", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n\r\n  \n")]
    [InlineData("﻿")]
    public async Task EmptyOrBlankFile_Throws(string content)
    {
        await Assert.ThrowsAsync<CsvImportException>(() => ReadAsync(content));
    }

    [Fact]
    public async Task EmptyByteArray_Throws()
    {
        await Assert.ThrowsAsync<CsvImportException>(() => ReadBytesAsync([]));
    }

    [Fact]
    public async Task HeaderOnly_Throws()
    {
        var ex = await Assert.ThrowsAsync<CsvImportException>(() => ReadAsync("name,email\r\n"));

        Assert.Contains("no data rows", ex.Message);
    }

    [Fact]
    public async Task UnterminatedQuote_Throws()
    {
        await Assert.ThrowsAsync<CsvImportException>(() => ReadAsync("name\n\"Ada\n"));
    }

    [Fact]
    public async Task NonUtf8Bytes_Throw()
    {
        var bytes = Encoding.Latin1.GetBytes("name\nJosé\n").Concat(new byte[] { 0xFF }).ToArray();

        await Assert.ThrowsAsync<CsvImportException>(() => ReadBytesAsync(bytes));
    }
}
