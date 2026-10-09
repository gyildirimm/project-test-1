using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace TodoApi.Tests;

public class CustomerImportApiTests : IDisposable
{
    private const string Header = "name,email,phone\r\n";

    private readonly TempDb _db = new();
    private readonly CustomerApiFactory _factory;
    private readonly HttpClient _client;

    public CustomerImportApiTests()
    {
        _factory = new CustomerApiFactory(_db.Path);
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        _db.Dispose();
    }

    private static MultipartFormDataContent Upload(string csv, string fileName = "customers.csv")
    {
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", fileName);
        return content;
    }

    private Task<HttpResponseMessage> PreviewAsync(string csv) =>
        _client.PostAsync("/api/customer-imports/preview", Upload(csv));

    private Task<HttpResponseMessage> CommitAsync(string csv, string fileName = "customers.csv") =>
        _client.PostAsync("/api/customer-imports", Upload(csv, fileName));

    private async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<int> CustomerCountAsync()
    {
        var customers = await _client.GetFromJsonAsync<JsonElement>("/api/customers");
        return customers.GetArrayLength();
    }

    private async Task<int> HistoryCountAsync()
    {
        var imports = await _client.GetFromJsonAsync<JsonElement>("/api/customer-imports");
        return imports.GetArrayLength();
    }

    private static string StatusOf(JsonElement row) => row.GetProperty("status").GetString()!;

    // ---------- Preview ----------

    [Fact]
    public async Task Preview_ClassifiesEveryRow_WithCountsAndReasons()
    {
        await _client.PostAsJsonAsync("/api/customers", new { name = "Grace Hopper", email = "grace@example.com" });
        var csv = Header +
            "Ada Lovelace,ada@example.com,111\r\n" +   // 2 importable
            "  ,bad@example.com,222\r\n" +             // 3 invalid: name required
            "Ada Again,ADA@example.com,333\r\n" +      // 4 duplicate in file of line 2
            "Grace Hopper,grace@example.com,\r\n" +    // 5 already exists
            "Linus,not-an-email,\r\n";                 // 6 invalid: email

        var response = await PreviewAsync(csv);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonOf(response);
        Assert.Equal(5, body.GetProperty("totalRows").GetInt32());
        Assert.Equal(1, body.GetProperty("importableCount").GetInt32());
        Assert.Equal(2, body.GetProperty("invalidCount").GetInt32());
        Assert.Equal(1, body.GetProperty("duplicateInFileCount").GetInt32());
        Assert.Equal(1, body.GetProperty("alreadyExistsCount").GetInt32());

        var rows = body.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal(["Importable", "Invalid", "DuplicateInFile", "AlreadyExists", "Invalid"],
            rows.Select(StatusOf));
        Assert.Equal([2, 3, 4, 5, 6], rows.Select(r => r.GetProperty("lineNumber").GetInt32()));

        Assert.Equal("Name is required.", rows[1].GetProperty("reason").GetString());
        Assert.Equal(2, rows[2].GetProperty("duplicateOfLineNumber").GetInt32());
        Assert.Equal(JsonValueKind.Null, rows[0].GetProperty("duplicateOfLineNumber").ValueKind);
        Assert.Equal(JsonValueKind.Null, rows[0].GetProperty("customerId").ValueKind);
        Assert.Equal("Ada Lovelace", rows[0].GetProperty("name").GetString());
        Assert.Equal("111", rows[0].GetProperty("phone").GetString());
    }

    [Fact]
    public async Task Preview_WritesNothing()
    {
        await _client.PostAsJsonAsync("/api/customers", new { name = "Grace Hopper", email = "grace@example.com" });
        var customersBefore = await CustomerCountAsync();

        var response = await PreviewAsync(Header + "Ada,ada@example.com,\r\nLinus,,\r\n");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(customersBefore, await CustomerCountAsync());
        Assert.Equal(0, await HistoryCountAsync());
    }

    // ---------- 400 paths (shared by preview and commit) ----------

    [Theory]
    [InlineData("email,phone\r\nada@example.com,1\r\n", "'name' column")]
    [InlineData("", "empty")]
    [InlineData("name,email\r\n", "no data rows")]
    [InlineData("name\r\n\"Ada\r\n", "unterminated")]
    public async Task InvalidCsv_Returns400WithErrorBody_ForPreviewAndCommit(string csv, string expectedMessagePart)
    {
        foreach (var response in new[] { await PreviewAsync(csv), await CommitAsync(csv) })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await JsonOf(response);
            Assert.Contains(expectedMessagePart, body.GetProperty("error").GetString());
        }

        Assert.Equal(0, await HistoryCountAsync());
        Assert.Equal(0, await CustomerCountAsync());
    }

    [Fact]
    public async Task NonUtf8File_Returns400()
    {
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(new byte[] { (byte)'n', (byte)'a', (byte)'m', (byte)'e', (byte)'\n', 0xFF }),
            "file", "bad.csv");

        var response = await _client.PostAsync("/api/customer-imports/preview", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MissingFileField_Returns400()
    {
        var content = new MultipartFormDataContent();
        content.Add(new StringContent("name\nAda\n"), "notfile");

        var response = await _client.PostAsync("/api/customer-imports", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("'file'", (await JsonOf(response)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task NotMultipart_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/customer-imports/preview", new { name = "Ada" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- Commit ----------

    [Fact]
    public async Task Commit_InsertsOnlyImportableRows_AndReturns201WithDetail()
    {
        await _client.PostAsJsonAsync("/api/customers", new { name = "Grace Hopper", email = "grace@example.com" });
        var csv = Header +
            "Çağla Şahin,cagla@example.com,555\r\n" +
            "  ,bad@example.com,\r\n" +
            "Ada Again,cagla@EXAMPLE.com,\r\n" +
            "Grace Hopper,grace@example.com,\r\n" +
            "İlker Öztürk,,555-0100\r\n";

        var response = await CommitAsync(csv, fileName: "members.csv");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await JsonOf(response);
        var id = body.GetProperty("id").GetInt32();
        Assert.Equal($"/api/customer-imports/{id}", response.Headers.Location?.OriginalString);

        Assert.Equal("members.csv", body.GetProperty("fileName").GetString());
        Assert.Equal(5, body.GetProperty("totalRows").GetInt32());
        Assert.Equal(2, body.GetProperty("importedCount").GetInt32());
        Assert.Equal(1, body.GetProperty("invalidCount").GetInt32());
        Assert.Equal(1, body.GetProperty("duplicateInFileCount").GetInt32());
        Assert.Equal(1, body.GetProperty("alreadyExistsCount").GetInt32());

        var rows = body.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal(["Imported", "Invalid", "DuplicateInFile", "AlreadyExists", "Imported"], rows.Select(StatusOf));
        Assert.Equal([2, 3, 4, 5, 6], rows.Select(r => r.GetProperty("lineNumber").GetInt32()));
        Assert.Equal(2, rows[2].GetProperty("duplicateOfLineNumber").GetInt32());

        var customers = await _client.GetFromJsonAsync<JsonElement>("/api/customers");
        var names = customers.EnumerateArray().Select(c => c.GetProperty("name").GetString()).ToList();
        Assert.Equal(["Grace Hopper", "Çağla Şahin", "İlker Öztürk"], names);

        // Imported rows point at the customer they created.
        var importedIds = rows.Where(r => StatusOf(r) == "Imported").Select(r => r.GetProperty("customerId").GetInt32());
        Assert.Equal(customers.EnumerateArray().Skip(1).Select(c => c.GetProperty("id").GetInt32()), importedIds);
    }

    [Fact]
    public async Task Commit_WithNothingImportable_StillRecordsHistory()
    {
        var response = await CommitAsync(Header + ",ada@example.com,\r\nLinus,not-an-email,\r\n");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await JsonOf(response);
        Assert.Equal(0, body.GetProperty("importedCount").GetInt32());
        Assert.Equal(0, await CustomerCountAsync());
        Assert.Equal(1, await HistoryCountAsync());
    }

    [Fact]
    public async Task Commit_RechecksDatabase_AgainstStateAtCommitTime()
    {
        var csv = Header + "Ada,ada@example.com,\r\nGrace,grace@example.com,\r\n";
        var preview = await JsonOf(await PreviewAsync(csv));
        Assert.Equal(2, preview.GetProperty("importableCount").GetInt32());

        // Someone adds Ada between preview and confirm.
        await _client.PostAsJsonAsync("/api/customers", new { name = "Ada", email = "ADA@example.com" });

        var body = await JsonOf(await CommitAsync(csv));

        Assert.Equal(1, body.GetProperty("importedCount").GetInt32());
        var rows = body.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal("AlreadyExists", StatusOf(rows[0]));
        Assert.Equal("Imported", StatusOf(rows[1]));
        Assert.Equal(2, await CustomerCountAsync());
    }

    // ---------- History ----------

    [Fact]
    public async Task History_ListsNewestFirst_WithSummaryFields()
    {
        var first = await JsonOf(await CommitAsync(Header + "Ada,ada@example.com,\r\n", "first.csv"));
        var second = await JsonOf(await CommitAsync(Header + "Linus,not-an-email,\r\n", "second.csv"));

        var list = await _client.GetFromJsonAsync<JsonElement>("/api/customer-imports");

        var items = list.EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal(second.GetProperty("id").GetInt32(), items[0].GetProperty("id").GetInt32());
        Assert.Equal(first.GetProperty("id").GetInt32(), items[1].GetProperty("id").GetInt32());

        var summary = items[0];
        Assert.Equal("second.csv", summary.GetProperty("fileName").GetString());
        Assert.True(summary.TryGetProperty("createdAt", out _));
        Assert.Equal(1, summary.GetProperty("totalRows").GetInt32());
        Assert.Equal(0, summary.GetProperty("importedCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("invalidCount").GetInt32());
        Assert.Equal(0, summary.GetProperty("duplicateInFileCount").GetInt32());
        Assert.Equal(0, summary.GetProperty("alreadyExistsCount").GetInt32());
        Assert.False(summary.TryGetProperty("rows", out _));
    }

    [Fact]
    public async Task Detail_ReturnsSummaryAndRowsOrderedByLine_IncludingErrorReport()
    {
        var created = await JsonOf(await CommitAsync(
            Header + "Ada,ada@example.com,\r\n,bad,\r\nAda2,ADA@example.com,\r\n", "mixed.csv"));
        // Line 3 is ",bad," (blank name, bad email) so it is Invalid with both messages.
        var id = created.GetProperty("id").GetInt32();

        var response = await _client.GetAsync($"/api/customer-imports/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonOf(response);
        Assert.Equal(id, body.GetProperty("id").GetInt32());
        Assert.Equal("mixed.csv", body.GetProperty("fileName").GetString());
        Assert.Equal(1, body.GetProperty("importedCount").GetInt32());

        var rows = body.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal([2, 3, 4], rows.Select(r => r.GetProperty("lineNumber").GetInt32()));
        Assert.Equal(["Imported", "Invalid", "DuplicateInFile"], rows.Select(StatusOf));

        // Error report: rows filtered by Invalid.
        var errors = rows.Where(r => StatusOf(r) == "Invalid").ToList();
        Assert.Single(errors);
        Assert.Contains("Name is required.", errors[0].GetProperty("reason").GetString());
        Assert.Equal("bad", errors[0].GetProperty("email").GetString());
    }

    [Fact]
    public async Task Detail_UnknownId_Returns404WithErrorBody()
    {
        var response = await _client.GetAsync("/api/customer-imports/999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("999", (await JsonOf(response)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Detail_Preserves_RawCellsAsUploaded()
    {
        var created = await JsonOf(await CommitAsync(Header + "  Ada  ,ADA@Example.com, +1 555 \r\n"));
        var id = created.GetProperty("id").GetInt32();

        var row = (await JsonOf(await _client.GetAsync($"/api/customer-imports/{id}")))
            .GetProperty("rows")[0];

        Assert.Equal("  Ada  ", row.GetProperty("name").GetString());
        Assert.Equal("ADA@Example.com", row.GetProperty("email").GetString());
        Assert.Equal(" +1 555 ", row.GetProperty("phone").GetString());
    }
}
