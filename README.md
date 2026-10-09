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

## Run the app (API and UI together)

The UI is plain HTML/CSS/JS in `frontend/` with no build step. Run the two in
separate terminals:

```bash
# terminal 1: API on http://localhost:5000
dotnet run --project src/TodoApi --urls http://localhost:5000

# terminal 2: UI on http://localhost:8080
cd frontend && node serve.mjs
```

Open http://localhost:8080. The UI calls `API_BASE` in `frontend/app.js`, which
defaults to `http://localhost:5000/api/customers`; change it if the API runs
elsewhere. The API allows cross-origin calls from `localhost` and `127.0.0.1` only.

## Test

```bash
dotnet test tests/TodoApi.Tests                  # API tests

cd frontend && npm install                       # UI tests (Playwright)
npx playwright install chromium                  # first time only
npm test
```

API tests run the real app against temporary SQLite files; they do not touch your data.
UI tests run in a browser against a mocked API, so no backend is needed.

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

## CSV import

Bulk-add customers from a CSV file. Upload is always two steps: preview first, then commit.
Nothing is written by the preview.

- `POST /api/customer-imports/preview`: multipart form, field `file`. Returns `200` with the
  classification of every row and writes nothing.
- `POST /api/customer-imports`: same upload. Re-classifies against the database at this moment,
  inserts only `Importable` rows and records the run in history, in one transaction. Returns
  `201` with `Location: /api/customer-imports/{id}` and the detail body. A run with zero
  imported rows is still recorded.
- `GET /api/customer-imports`: history, newest first (summaries only).
- `GET /api/customer-imports/{id}`: one run with its rows ordered by `lineNumber`, or `404`.
  The error report is the rows whose `status` is `Invalid`.

File format: UTF-8 (a BOM is allowed), comma-delimited, double-quoted fields (`""` for a
literal quote, line breaks allowed inside quotes). The header row is required and matched
case-insensitively. `name` is required; `email` and `phone` are optional; other columns are
ignored. Blank lines are skipped. A file with no `name` column, no content, or only a header
returns `400` with `{ "error": "..." }`. Line numbers are physical line numbers, the header is line 1.

Row rules are the same as customer create: name is required after trimming, a non-blank email
must match the customer email check, and phone is trimmed.

Row statuses:

| Status           | Meaning |
|------------------|---------|
| `Importable`     | Valid and new. Inserted on commit. Preview only. |
| `Imported`       | Inserted by this run. Commit and history only. `customerId` is set. |
| `Invalid`        | Fails validation. `reason` explains why. |
| `DuplicateInFile`| Same key as an earlier valid row. `duplicateOfLineNumber` points to that row. |
| `AlreadyExists`  | Same key as a customer already in the database. |

**Duplicate key.** A row with an email is identified by its lowercased, trimmed email. A row
without an email is identified by its lowercased name plus its trimmed phone. Keys are compared
the same way for file rows and existing customers, so a customer saved with an email is not
matched by a file row that has no email, even with the same name and phone.

Upload response shapes:

```json
{
  "totalRows": 3, "importableCount": 1, "invalidCount": 1, "duplicateInFileCount": 0, "alreadyExistsCount": 1,
  "rows": [
    { "lineNumber": 2, "name": "Ada", "email": "ada@example.com", "phone": null,
      "status": "Importable", "reason": null, "duplicateOfLineNumber": null, "customerId": null }
  ]
}
```

History summary and detail:

```json
{ "id": 1, "fileName": "members.csv", "createdAt": "2026-10-09T12:00:00Z", "totalRows": 3,
  "importedCount": 1, "invalidCount": 1, "duplicateInFileCount": 0, "alreadyExistsCount": 1 }
```

The detail adds `"rows": [...]` with the same row shape, where `status` is `Imported` for inserted rows.
The `name`, `email` and `phone` values are the raw cell values as uploaded. The file itself is not stored.

```bash
curl -F "file=@members.csv" http://localhost:5000/api/customer-imports/preview
curl -F "file=@members.csv" http://localhost:5000/api/customer-imports
curl http://localhost:5000/api/customer-imports
curl http://localhost:5000/api/customer-imports/1
```

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
