using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace TodoApi.Tests;

public class CustomerApiTests : IDisposable
{
    private readonly TempDb _db = new();
    private readonly CustomerApiFactory _factory;
    private readonly HttpClient _client;

    public CustomerApiTests()
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

    // ---------- Create ----------

    [Fact]
    public async Task Create_ReturnsCreatedWithCustomerAndLocation()
    {
        var response = await _client.PostAsJsonAsync("/api/customers",
            new { name = "  Ada Lovelace  ", email = "ada@example.com", phone = "+44 20 7946 0000" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("id").GetInt32());
        Assert.Equal("Ada Lovelace", body.GetProperty("name").GetString());
        Assert.Equal("ada@example.com", body.GetProperty("email").GetString());
        Assert.Equal("+44 20 7946 0000", body.GetProperty("phone").GetString());
        Assert.Equal(body.GetProperty("createdAt").GetDateTime(), body.GetProperty("updatedAt").GetDateTime());
        Assert.Equal("/api/customers/1", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Create_WithOnlyName_LeavesEmailAndPhoneNull()
    {
        var response = await _client.PostAsJsonAsync("/api/customers", new { name = "Grace" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("email").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("phone").ValueKind);
    }

    [Fact]
    public async Task Create_BlankEmailAndPhone_AreStoredAsNull()
    {
        var response = await _client.PostAsJsonAsync("/api/customers",
            new { name = "Grace", email = "   ", phone = "" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("email").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("phone").ValueKind);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"name\":null}")]
    [InlineData("{\"name\":\"\"}")]
    [InlineData("{\"name\":\"   \"}")]
    public async Task Create_MissingOrBlankName_Returns400WithFieldError(string json)
    {
        var response = await PostRaw("/api/customers", json);

        await AssertValidationError(response, "name");
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("a@b")]
    [InlineData("a b@example.com")]
    [InlineData("a@@example.com")]
    public async Task Create_InvalidEmail_Returns400WithFieldError(string email)
    {
        var response = await _client.PostAsJsonAsync("/api/customers", new { name = "X", email });

        await AssertValidationError(response, "email");
    }

    [Fact]
    public async Task Create_ReportsAllInvalidFieldsTogether()
    {
        var response = await _client.PostAsJsonAsync("/api/customers", new { name = "", email = "bad" });

        var body = await AssertValidationError(response, "name");
        Assert.True(body.GetProperty("errors").TryGetProperty("email", out _));
    }

    [Fact]
    public async Task Create_MalformedJson_Returns400WithErrorBody()
    {
        var response = await PostRaw("/api/customers", "{\"name\": ");

        await AssertBadRequestError(response);
    }

    [Fact]
    public async Task Create_WrongFieldType_Returns400WithErrorBody()
    {
        var response = await PostRaw("/api/customers", "{\"name\": 123}");

        await AssertBadRequestError(response);
    }

    [Fact]
    public async Task Create_JsonArrayBody_Returns400WithErrorBody()
    {
        var response = await PostRaw("/api/customers", "[1,2,3]");

        await AssertBadRequestError(response);
    }

    [Fact]
    public async Task Create_EmptyBody_Returns400WithErrorBody()
    {
        var response = await PostRaw("/api/customers", "");

        await AssertBadRequestError(response);
    }

    // ---------- List and search ----------

    [Fact]
    public async Task List_WhenEmpty_ReturnsEmptyArray()
    {
        var response = await _client.GetAsync("/api/customers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        Assert.Equal(0, body.GetArrayLength());
    }

    [Fact]
    public async Task List_WithoutSearch_ReturnsAllInIdOrder()
    {
        await CreateCustomer("Alice", "alice@example.com");
        await CreateCustomer("Bob");

        var names = await SearchNames(null);

        Assert.Equal(new[] { "Alice", "Bob" }, names);
    }

    [Theory]
    [InlineData("ali", new[] { "Alice" })]
    [InlineData("ALI", new[] { "Alice" })]
    [InlineData("ice", new[] { "Alice" })]
    [InlineData("bob@", new[] { "Bobby" })]
    [InlineData("EXAMPLE.ORG", new[] { "Bobby", "Carol" })]
    [InlineData("zzz", new string[0])]
    public async Task Search_MatchesNameOrEmailCaseInsensitively(string term, string[] expected)
    {
        await CreateCustomer("Alice", "alice@example.com");
        await CreateCustomer("Bobby", "bob@example.org");
        await CreateCustomer("Carol", "carol@example.org");

        var names = await SearchNames(term);

        Assert.Equal(expected, names);
    }

    [Fact]
    public async Task Search_NoMatch_ReturnsEmptyArrayNotError()
    {
        await CreateCustomer("Alice");

        var response = await _client.GetAsync("/api/customers?search=nobody");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetArrayLength());
    }

    [Fact]
    public async Task Search_MatchesNonAsciiNamesCaseInsensitively()
    {
        await CreateCustomer("Özgür Çelik");

        Assert.Equal(new[] { "Özgür Çelik" }, await SearchNames("ÇELIK"));
        Assert.Equal(new[] { "Özgür Çelik" }, await SearchNames("özgür"));
    }

    [Fact]
    public async Task Search_BlankTerm_ReturnsAll()
    {
        await CreateCustomer("Alice");
        await CreateCustomer("Bob");

        var names = await SearchNames("   ");

        Assert.Equal(new[] { "Alice", "Bob" }, names);
    }

    // ---------- Get by id ----------

    [Fact]
    public async Task Get_ExistingId_Returns200()
    {
        var id = await CreateCustomer("Alice", "alice@example.com");

        var response = await _client.GetAsync($"/api/customers/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(id, body.GetProperty("id").GetInt32());
        Assert.Equal("Alice", body.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Get_UnknownId_Returns404WithErrorBody()
    {
        var response = await _client.GetAsync("/api/customers/999");

        await AssertNotFoundError(response);
    }

    // ---------- Update ----------

    [Fact]
    public async Task Update_ExistingId_Returns200WithNewValues()
    {
        var id = await CreateCustomer("Alice", "alice@example.com");

        var response = await _client.PutAsJsonAsync($"/api/customers/{id}",
            new { name = "Alicia", email = "alicia@example.org", phone = "555-0100" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Alicia", body.GetProperty("name").GetString());
        Assert.Equal("alicia@example.org", body.GetProperty("email").GetString());
        Assert.Equal("555-0100", body.GetProperty("phone").GetString());
    }

    [Fact]
    public async Task Update_BumpsUpdatedAt_AndKeepsCreatedAt()
    {
        var createdResponse = await _client.PostAsJsonAsync("/api/customers", new { name = "Alice" });
        var created = await createdResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetInt32();

        await Task.Delay(20);
        var response = await _client.PutAsJsonAsync($"/api/customers/{id}", new { name = "Alicia" });

        var updated = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(created.GetProperty("createdAt").GetDateTime(), updated.GetProperty("createdAt").GetDateTime());
        Assert.True(updated.GetProperty("updatedAt").GetDateTime() > created.GetProperty("updatedAt").GetDateTime());
    }

    [Fact]
    public async Task Update_OmittedEmailAndPhone_ClearsThem()
    {
        var id = await CreateCustomer("Alice", "alice@example.com");

        var response = await _client.PutAsJsonAsync($"/api/customers/{id}", new { name = "Alice" });

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("email").ValueKind);
    }

    [Fact]
    public async Task Update_UnknownId_Returns404WithErrorBody()
    {
        var response = await _client.PutAsJsonAsync("/api/customers/999", new { name = "Ghost" });

        await AssertNotFoundError(response);
    }

    [Fact]
    public async Task Update_BlankName_Returns400AndLeavesRecordUnchanged()
    {
        var id = await CreateCustomer("Alice");

        var response = await _client.PutAsJsonAsync($"/api/customers/{id}", new { name = " " });

        await AssertValidationError(response, "name");
        var current = await _client.GetFromJsonAsync<JsonElement>($"/api/customers/{id}");
        Assert.Equal("Alice", current.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Update_InvalidEmail_Returns400WithFieldError()
    {
        var id = await CreateCustomer("Alice");

        var response = await _client.PutAsJsonAsync($"/api/customers/{id}", new { name = "Alice", email = "nope" });

        await AssertValidationError(response, "email");
    }

    [Fact]
    public async Task Update_WrongFieldType_Returns400WithErrorBody()
    {
        var id = await CreateCustomer("Alice");

        var response = await PutRaw($"/api/customers/{id}", "{\"name\": \"Alice\", \"phone\": {}}");

        await AssertBadRequestError(response);
    }

    // ---------- Delete ----------

    [Fact]
    public async Task Delete_ExistingId_Returns204AndRemovesRecord()
    {
        var id = await CreateCustomer("Alice");

        var response = await _client.DeleteAsync($"/api/customers/{id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/customers/{id}")).StatusCode);
    }

    [Fact]
    public async Task Delete_UnknownId_Returns404WithErrorBody()
    {
        var response = await _client.DeleteAsync("/api/customers/999");

        await AssertNotFoundError(response);
    }

    // ---------- Unexpected failures ----------

    [Fact]
    public async Task UnexpectedFailure_Returns500WithGenericMessage()
    {
        // Break the schema under a running app so the next request fails inside the handler.
        using (var raw = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _db.Path }.ToString()))
        {
            await raw.OpenAsync();
            using var cmd = raw.CreateCommand();
            cmd.CommandText = "DROP TABLE Customers";
            await cmd.ExecuteNonQueryAsync();
        }

        var response = await _client.GetAsync("/api/customers");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("An unexpected error occurred.", body.GetProperty("error").GetString());
        Assert.DoesNotContain("Customers", await response.Content.ReadAsStringAsync());
    }

    // ---------- Persistence ----------

    [Fact]
    public async Task Data_SurvivesRestartOfTheApp()
    {
        var id = await CreateCustomer("Alice", "alice@example.com");
        _client.Dispose();
        _factory.Dispose();
        SqliteConnection.ClearAllPools();

        // A new app instance pointed at the same file.
        using var restarted = new CustomerApiFactory(_db.Path);
        using var client = restarted.CreateClient();

        var response = await client.GetAsync($"/api/customers/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Alice", body.GetProperty("name").GetString());
        Assert.Equal("alice@example.com", body.GetProperty("email").GetString());

        // IDs continue from the stored data rather than restarting at 1.
        var created = await client.PostAsJsonAsync("/api/customers", new { name = "Bob" });
        var next = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(id + 1, next.GetProperty("id").GetInt32());
    }

    // ---------- Helpers ----------

    private async Task<int> CreateCustomer(string name, string? email = null)
    {
        var response = await _client.PostAsJsonAsync("/api/customers", new { name, email });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetInt32();
    }

    private async Task<List<string>> SearchNames(string? term)
    {
        var url = term is null
            ? "/api/customers"
            : $"/api/customers?search={Uri.EscapeDataString(term)}";
        var response = await _client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.EnumerateArray().Select(c => c.GetProperty("name").GetString()!).ToList();
    }

    private Task<HttpResponseMessage> PostRaw(string url, string json) =>
        _client.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));

    private Task<HttpResponseMessage> PutRaw(string url, string json) =>
        _client.PutAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));

    private static async Task<JsonElement> AssertValidationError(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Validation failed.", body.GetProperty("error").GetString());
        Assert.True(body.GetProperty("errors").TryGetProperty(field, out var messages));
        Assert.True(messages.GetArrayLength() > 0);
        return body;
    }

    private static async Task AssertBadRequestError(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("error").GetString()));
    }

    private static async Task AssertNotFoundError(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("error").GetString()));
    }

    // ---------- CORS ----------

    [Theory]
    [InlineData("http://localhost:8080")]
    [InlineData("http://127.0.0.1:5500")]
    public async Task Cors_AllowsLocalOrigins(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/customers");
        request.Headers.Add("Origin", origin);

        var response = await _client.SendAsync(request);

        Assert.Equal(origin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task Cors_DoesNotAllowOtherOrigins()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/customers");
        request.Headers.Add("Origin", "https://evil.example");

        var response = await _client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
