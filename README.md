# Customer API

A small customer tracking service: a .NET 8 minimal API with EF Core and SQLite.
Customer records persist in a SQLite file, so they survive restarts.

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Install

```bash
dotnet restore
```

## Start

```bash
dotnet run --project src/TodoApi
```

The app applies pending database migrations on startup. The API listens on the
address printed to the console (see `src/TodoApi/Properties/launchSettings.json`
for the default). To pick a port:

```bash
dotnet run --project src/TodoApi --urls http://localhost:5000
```

## Test

```bash
dotnet test tests/TodoApi.Tests
```

Tests run the real app against temporary SQLite files; they do not touch your data.

## Environment variables

| Variable           | Default                                   | Purpose                                      |
|--------------------|-------------------------------------------|----------------------------------------------|
| `CUSTOMERS_DB_PATH`| `src/TodoApi/customers.db` (project dir)  | Path to the SQLite database file. Created if missing. |

Example:

```bash
CUSTOMERS_DB_PATH=/var/lib/customers/customers.db dotnet run --project src/TodoApi
```

## Endpoints

All bodies are JSON. Timestamps are UTC ISO 8601.

| Method | Path                    | Success                | Errors                  |
|--------|-------------------------|------------------------|-------------------------|
| GET    | `/api/customers?search=`| `200` array            | —                       |
| POST   | `/api/customers`        | `201` customer         | `400`                   |
| GET    | `/api/customers/{id}`   | `200` customer         | `404`                   |
| PUT    | `/api/customers/{id}`   | `200` customer         | `400`, `404`            |
| DELETE | `/api/customers/{id}`   | `204` no content       | `404`                   |

Any endpoint can also return `500` with `{"error":"An unexpected error occurred."}`.

### Customer

```json
{
  "id": 1,
  "name": "Ada Lovelace",
  "email": "ada@example.com",
  "phone": "+44 20 7946 0000",
  "createdAt": "2026-10-09T12:00:00Z",
  "updatedAt": "2026-10-09T12:00:00Z"
}
```

`id`, `createdAt` and `updatedAt` are set by the server. `email` and `phone` are
`null` when absent.

### Request body (POST and PUT)

```json
{ "name": "Ada Lovelace", "email": "ada@example.com", "phone": "+44 20 7946 0000" }
```

- `name`: required; must be non-empty after trimming.
- `email`: optional; if non-blank it must look like `local@domain.tld` (no spaces, one `@`).
- `phone`: optional; stored as given (trimmed), no format check.
- Blank `email`/`phone` are stored as `null`.
- PUT replaces the record: omitted or blank `email`/`phone` are cleared.

### Search

`GET /api/customers?search=ada` returns customers whose `name` or `email` contains
the text, ignoring case. A blank or missing `search` returns all customers. No match
returns `[]`. Results are ordered by `id`.

### Errors

Validation failures (`400`):

```json
{ "error": "Validation failed.", "errors": { "name": ["Name is required."] } }
```

Malformed JSON, a non-object body or a wrong field type (e.g. `"name": 123`) returns
`400` with `{ "error": "Request body must be valid JSON with string fields: name, email, phone." }`.

Unknown ids return `404` with `{ "error": "Customer 42 was not found." }`.

## Examples

```bash
curl -X POST http://localhost:5000/api/customers \
  -H "Content-Type: application/json" \
  -d '{"name":"Ada Lovelace","email":"ada@example.com"}'

curl "http://localhost:5000/api/customers?search=ada"
curl http://localhost:5000/api/customers/1
curl -X PUT http://localhost:5000/api/customers/1 \
  -H "Content-Type: application/json" \
  -d '{"name":"Ada Lovelace","phone":"555-0100"}'
curl -X DELETE http://localhost:5000/api/customers/1
```

## Database migrations

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project src/TodoApi --output-dir Migrations
```
