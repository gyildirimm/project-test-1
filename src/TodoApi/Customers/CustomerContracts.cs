using System.Text.RegularExpressions;
using TodoApi.Data;

namespace TodoApi.Customers;

// Request body. Fields are strings so that wrong JSON types surface as a 400 from the reader.
public record CustomerRequest(string? Name, string? Email, string? Phone);

public record CustomerResponse(int Id, string Name, string? Email, string? Phone, DateTime CreatedAt, DateTime UpdatedAt)
{
    public static CustomerResponse From(Customer c) =>
        new(c.Id, c.Name, c.Email, c.Phone, c.CreatedAt, c.UpdatedAt);
}

public record ErrorResponse(string Error, IDictionary<string, string[]>? Errors = null);

public sealed record ValidCustomer(string Name, string? Email, string? Phone);

public static class CustomerValidator
{
    // Basic shape check only: something@something.tld with no whitespace or extra '@'.
    private static readonly Regex EmailPattern = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    // Returns the normalized values, or null with field errors in `errors`.
    public static ValidCustomer? Validate(CustomerRequest request, out Dictionary<string, string[]> errors)
    {
        errors = [];
        var name = request.Name?.Trim();
        var email = NullIfEmpty(request.Email);
        var phone = NullIfEmpty(request.Phone);

        if (string.IsNullOrEmpty(name))
            errors["name"] = ["Name is required."];
        if (email is not null && !EmailPattern.IsMatch(email))
            errors["email"] = ["Email must be a valid email address."];

        if (errors.Count > 0)
            return null;
        return new ValidCustomer(name!, email, phone);
    }

    private static string? NullIfEmpty(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
