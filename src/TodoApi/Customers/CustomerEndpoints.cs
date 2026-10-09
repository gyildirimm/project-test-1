using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using TodoApi.Data;

namespace TodoApi.Customers;

public static class CustomerEndpoints
{
    public static RouteGroupBuilder MapCustomerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/customers");

        group.MapGet("/", ListCustomers);
        group.MapPost("/", CreateCustomer);
        group.MapGet("/{id:int}", GetCustomer);
        group.MapPut("/{id:int}", UpdateCustomer);
        group.MapDelete("/{id:int}", DeleteCustomer);

        return group;
    }

    // Case-insensitive substring match on name or email. Done in memory because SQLite's LIKE
    // only folds ASCII case, which would miss e.g. "İ"/"ı" in Turkish names.
    private static async Task<Ok<List<CustomerResponse>>> ListCustomers(string? search, CustomerDbContext db)
    {
        var customers = await db.Customers.AsNoTracking().OrderBy(c => c.Id).ToListAsync();
        var term = search?.Trim();

        if (!string.IsNullOrEmpty(term))
        {
            customers = customers
                .Where(c => c.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || (c.Email?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }

        return TypedResults.Ok(customers.Select(CustomerResponse.From).ToList());
    }

    private static async Task<IResult> CreateCustomer(HttpRequest http, CustomerDbContext db)
    {
        var (request, badRequest) = await ReadBody(http);
        if (badRequest is not null)
            return badRequest;

        var valid = CustomerValidator.Validate(request!, out var errors);
        if (valid is null)
            return ValidationFailed(errors);

        var now = DateTime.UtcNow;
        var customer = new Customer
        {
            Name = valid.Name,
            Email = valid.Email,
            Phone = valid.Phone,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        return TypedResults.Created($"/api/customers/{customer.Id}", CustomerResponse.From(customer));
    }

    private static async Task<IResult> GetCustomer(int id, CustomerDbContext db)
    {
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        return customer is null ? NotFoundFor(id) : TypedResults.Ok(CustomerResponse.From(customer));
    }

    // PUT replaces the whole resource: omitted or blank email/phone are cleared.
    private static async Task<IResult> UpdateCustomer(int id, HttpRequest http, CustomerDbContext db)
    {
        var (request, badRequest) = await ReadBody(http);
        if (badRequest is not null)
            return badRequest;

        var valid = CustomerValidator.Validate(request!, out var errors);
        if (valid is null)
            return ValidationFailed(errors);

        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id);
        if (customer is null)
            return NotFoundFor(id);

        customer.Name = valid.Name;
        customer.Email = valid.Email;
        customer.Phone = valid.Phone;
        customer.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return TypedResults.Ok(CustomerResponse.From(customer));
    }

    private static async Task<IResult> DeleteCustomer(int id, CustomerDbContext db)
    {
        var deleted = await db.Customers.Where(c => c.Id == id).ExecuteDeleteAsync();
        return deleted > 0 ? TypedResults.NoContent() : NotFoundFor(id);
    }

    // Reads the JSON body ourselves so malformed JSON or wrong field types get the same
    // error body as other 400s, instead of the framework's empty 400 response.
    private static async Task<(CustomerRequest? Request, IResult? Error)> ReadBody(HttpRequest http)
    {
        try
        {
            var request = await http.ReadFromJsonAsync<CustomerRequest>();
            if (request is null)
                return (null, TypedResults.BadRequest(new ErrorResponse("Request body is required.")));
            return (request, null);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or BadHttpRequestException)
        {
            return (null, TypedResults.BadRequest(new ErrorResponse(
                "Request body must be valid JSON with string fields: name, email, phone.")));
        }
    }

    private static IResult ValidationFailed(IDictionary<string, string[]> errors) =>
        TypedResults.BadRequest(new ErrorResponse("Validation failed.", errors));

    private static IResult NotFoundFor(int id) =>
        TypedResults.NotFound(new ErrorResponse($"Customer {id} was not found."));
}
