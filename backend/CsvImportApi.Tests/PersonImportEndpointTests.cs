using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CsvImportApi.People;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace CsvImportApi.Tests;

public class PersonImportEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PersonImportEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private HttpClient NewClient() => _factory.CreateClient();

    private static MultipartFormDataContent CsvUpload(string csv, string fileName = "people.csv")
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
        content.Add(file, PersonCsvContract.FormFieldName, fileName);
        return content;
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    [Fact]
    public async Task PreviewThenCommitImportsValidRowsAndListsThem()
    {
        var client = NewClient();
        const string csv = "first_name,last_name,email,phone\n" +
                           "Grace,Hopper,grace.preview@example.com,\n" +
                           "Alan,Turing,alan.preview@example.com,+44 20 7946 0000\n";

        var previewResponse = await client.PostAsync("/api/people/imports/preview", CsvUpload(csv));
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = await ReadJson(previewResponse);

        Assert.Equal(2, preview.GetProperty("summary").GetProperty("valid").GetInt32());
        Assert.Equal(2, preview.GetProperty("rows")[0].GetProperty("line").GetInt32());
        var importId = preview.GetProperty("importId").GetGuid();

        var commitResponse = await client.PostAsync($"/api/people/imports/{importId}/commit", null);
        Assert.Equal(HttpStatusCode.OK, commitResponse.StatusCode);
        var commit = await ReadJson(commitResponse);
        Assert.Equal(2, commit.GetProperty("imported").GetInt32());
        Assert.Equal(2, commit.GetProperty("createdIds").GetArrayLength());

        var people = await client.GetFromJsonAsync<List<PersonDto>>("/api/people");
        Assert.Contains(people!, p => p.Email == "grace.preview@example.com" && p.LastName == "Hopper");
    }

    [Fact]
    public async Task CommitTwiceReturnsAlreadyCommitted()
    {
        var client = NewClient();
        var preview = await ReadJson(await client.PostAsync("/api/people/imports/preview",
            CsvUpload("first_name,last_name,email\nMargaret,Hamilton,margaret.twice@example.com\n")));
        var url = $"/api/people/imports/{preview.GetProperty("importId").GetGuid()}/commit";

        await client.PostAsync(url, null);
        var second = await client.PostAsync(url, null);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("IMPORT_ALREADY_COMMITTED", (await ReadJson(second)).GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task CommitImportsValidRowsAndSkipsInvalidOnes()
    {
        var client = NewClient();
        var preview = await ReadJson(await client.PostAsync("/api/people/imports/preview",
            CsvUpload("first_name,last_name,email\nOk,Person,partial.valid@example.com\n,Missing,bad-email\n")));
        var url = $"/api/people/imports/{preview.GetProperty("importId").GetGuid()}/commit";

        var response = await client.PostAsync(url, null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var commit = await ReadJson(response);
        Assert.Equal(1, commit.GetProperty("imported").GetInt32());
        Assert.Equal(1, commit.GetProperty("skipped").GetProperty("invalid").GetInt32());
    }

    [Fact]
    public async Task UnknownImportIdIsNotFound()
    {
        var response = await NewClient().PostAsync($"/api/people/imports/{Guid.NewGuid()}/commit", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("IMPORT_NOT_FOUND", (await ReadJson(response)).GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task MissingFileFieldIsBadRequest()
    {
        var content = new MultipartFormDataContent { { new StringContent("x"), "other" } };

        var response = await NewClient().PostAsync("/api/people/imports/preview", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("CSV_FILE_MISSING", (await ReadJson(response)).GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task NonMultipartBodyIsBadRequest()
    {
        var response = await NewClient().PostAsync("/api/people/imports/preview",
            new StringContent("first_name,last_name,email"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OversizeFileIsRejectedWithJsonError()
    {
        var big = "first_name,last_name,email\n" + new string('a', PersonCsvContract.MaxFileBytes + 1);

        var response = await NewClient().PostAsync("/api/people/imports/preview", CsvUpload(big));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("CSV_FILE_TOO_LARGE", (await ReadJson(response)).GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task NonUtf8FileIsBadRequest()
    {
        var bytes = new byte[] { 0x66, 0x69, 0x72, 0x73, 0x74, 0x0A, 0xFF, 0xFE, 0xFD };
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(bytes), PersonCsvContract.FormFieldName, "bad.csv");

        var response = await NewClient().PostAsync("/api/people/imports/preview", content);

        Assert.Equal("CSV_INVALID_ENCODING", (await ReadJson(response)).GetProperty("error").GetProperty("code").GetString());
    }
}
