# Person CSV import contract

Base URL: the ASP.NET Core host (`dotnet run` in `backend/CsvImportApi`). CORS is open for all origins (matches the earlier todo-api).

Flow: **preview** (upload file, get validation report + `importId`) → **commit** (import the previewed valid rows by `importId`).

## Endpoints

### 1. `POST /api/people/imports/preview`

Request: `multipart/form-data` with exactly one file part named **`file`**. Any content type on the part is accepted; the content must be UTF-8 CSV.

Success: `200 OK`
```json
{
  "importId": "3f2b9c1e-0d4a-4a7e-9a55-1b2c3d4e5f60",
  "expiresAt": "2026-10-09T12:15:00+00:00",
  "summary": { "totalRows": 3, "valid": 1, "invalid": 1, "duplicateInFile": 1, "alreadyExists": 0 },
  "rows": [
    {
      "line": 2,
      "status": "valid",
      "values": { "firstName": "Ada", "lastName": "Lovelace", "email": "ada@example.com", "phone": "+90 555 123 45 67" },
      "errors": []
    },
    {
      "line": 3,
      "status": "invalid",
      "values": { "firstName": "", "lastName": "Hopper", "email": "not-an-email", "phone": null },
      "errors": [
        { "code": "REQUIRED", "field": "first_name", "message": "'first_name' is required." },
        { "code": "INVALID_EMAIL", "field": "email", "message": "'email' is not a valid address." }
      ]
    }
  ]
}
```

- `rows` contains every data row (in file order) with its physical file line (header = line 1). Values are trimmed. `phone` is `null` when absent or blank.
- `status` is one of: `valid`, `invalid`, `duplicate_in_file`, `already_exists` (see Dedupe).
- Row-level problems are **not** HTTP errors; they appear in `rows[].errors`.
- The preview is stored in memory for **15 minutes** (`expiresAt`).

### 2. `POST /api/people/imports/{importId}/commit`

No body. `importId` is the GUID from the preview.

Success: `200 OK`
```json
{
  "importId": "3f2b9c1e-0d4a-4a7e-9a55-1b2c3d4e5f60",
  "imported": 1,
  "skipped": { "duplicateInFile": 1, "alreadyExists": 0 },
  "createdIds": [1]
}
```

- Imports every row whose status was `valid` at preview time. Existence is re-checked at commit, so a row that became a duplicate in the meantime is counted under `skipped.alreadyExists` instead of being created.
- Duplicate-in-file rows and already-existing rows are skipped, not errors.
- A commit is **all-or-nothing on validity**: if the preview has any `invalid` row, the commit is refused (409) and nothing is imported. The user must fix the file and preview again.
- An import can be committed once.

### 3. `GET /api/people`

Returns all stored people (in-memory store):
```json
[ { "id": 1, "firstName": "Ada", "lastName": "Lovelace", "email": "ada@example.com", "phone": "+90 555 123 45 67" } ]
```

## CSV header contract

- Encoding: **UTF-8** (a leading BOM is allowed). Other encodings → `400 CSV_INVALID_ENCODING`.
- Delimiter: **comma** `,`. Quoted fields (`"..."`) may contain commas, line breaks, and `""` for a literal quote (RFC 4180). Line endings: LF, CRLF, or CR.
- The first non-blank line is the header. Header names are matched **case-insensitively** and **trimmed**; column order is free.

| Column       | Required | Max length | Rules                                                              |
|--------------|----------|------------|--------------------------------------------------------------------|
| `first_name` | yes      | 100        | non-empty after trim                                               |
| `last_name`  | yes      | 100        | non-empty after trim                                               |
| `email`      | yes      | 254        | non-empty; must match `^[^@\s]+@[^@\s]+\.[^@\s]+$`                 |
| `phone`      | no       | 30         | if non-empty: only digits, spaces, `+`, `-`, `(`, `)`; 7–15 digits; `+` only at start |

- Unknown header columns are **rejected** (`400 CSV_UNKNOWN_COLUMN`), not ignored, so typos cannot silently drop data.
- Duplicate header columns are rejected (`400 CSV_DUPLICATE_COLUMN`).
- Blank lines (and lines where every field is empty, e.g. `,,,`) are skipped and not counted.
- A row with a different number of fields than the header gets a row error `FIELD_COUNT_MISMATCH` (it is not parsed best-effort).

Example file:
```csv
first_name,last_name,email,phone
Ada,Lovelace,ada@example.com,"+90 555 123 45 67"
Grace,Hopper,grace@example.com,
```

## Dedupe rule

- **Key:** `email`, trimmed and lower-cased (invariant culture). `ADA@x.com` and ` ada@x.com ` are the same person.
- **Within a file:** the first valid occurrence is `valid`; later occurrences are `duplicate_in_file` and skipped. Invalid rows do not reserve their email.
- **Against stored people:** a valid row whose key already exists is `already_exists` and skipped. This check takes precedence over in-file duplication.
- Phone is not part of the key.

## Limits

| Limit                        | Value         | Enforced by                                         |
|------------------------------|---------------|-----------------------------------------------------|
| File size                    | 1 MiB (1,048,576 bytes) | `413 CSV_FILE_TOO_LARGE` (our code)      |
| Data rows (excluding header) | 1000          | `422 CSV_TOO_MANY_ROWS`                             |
| Preview lifetime             | 15 minutes    | `410 IMPORT_EXPIRED` on commit                      |

Requests whose multipart body exceeds about 1 MiB + 64 KiB are cut off by the framework before our code runs; that response may not be JSON. Clients should keep files under 1 MiB, which the limit above already requires.

## Error envelope

All errors (except the framework-level body-size cut-off above) use:
```json
{ "error": { "code": "CSV_UNKNOWN_COLUMN", "message": "...", "details": [ { "line": 1, "column": "company" } ] } }
```
`details` is optional and only present for some codes.

| HTTP | Code                       | When                                                              | `details`                          |
|------|----------------------------|-------------------------------------------------------------------|------------------------------------|
| 400  | `CSV_FILE_MISSING`         | Not multipart, or no `file` part                                   | —                                  |
| 400  | `CSV_EMPTY_FILE`           | Zero bytes, whitespace-only, or no header row                      | —                                  |
| 400  | `CSV_INVALID_ENCODING`     | Not valid UTF-8                                                    | —                                  |
| 400  | `CSV_MALFORMED`            | Unclosed quoted field                                              | `[{ line }]`                       |
| 400  | `CSV_MISSING_COLUMN`       | Required header missing                                            | `[{ column }]`                     |
| 400  | `CSV_UNKNOWN_COLUMN`       | Header column not in contract                                      | `[{ line, column }]`               |
| 400  | `CSV_DUPLICATE_COLUMN`     | Same header column twice                                           | `[{ line, column }]`               |
| 404  | `IMPORT_NOT_FOUND`         | Unknown `importId` (or never created)                              | —                                  |
| 409  | `IMPORT_ALREADY_COMMITTED` | Commit called again for the same `importId`                        | —                                  |
| 409  | `IMPORT_HAS_INVALID_ROWS`  | Commit when the preview has any `invalid` row                      | `[{ line, errors: RowError[] }]`   |
| 410  | `IMPORT_EXPIRED`           | Commit after the 15-minute preview window                          | —                                  |
| 413  | `CSV_FILE_TOO_LARGE`       | File over 1 MiB                                                    | —                                  |
| 422  | `CSV_TOO_MANY_ROWS`        | More than 1000 data rows                                           | —                                  |

Row error codes (in `rows[].errors[].code`): `REQUIRED`, `TOO_LONG`, `INVALID_EMAIL`, `INVALID_PHONE`, `FIELD_COUNT_MISMATCH`. `field` is the contract column name, or `null` for `FIELD_COUNT_MISMATCH`.

## Deliberately left out

- **Persistence:** people live in an in-memory repository (`IPersonRepository`). Restarting the server clears them. Previews are also in-memory, so a restart invalidates pending `importId`s. No EF Core/SQLite is wired yet, because the repo has no person/CRM domain or schema to build on.
- **Authentication / authorization:** none. Any caller can upload and commit.
- **Partial commits:** there is no "import only the valid rows" option; any invalid row blocks the commit.
- **Other delimiters** (`;`, tab), **other encodings** (UTF-16, Windows-1254), **Excel files**, and **extra columns** (e.g. company, address).
- **Phone normalization** (E.164 conversion): the phone is stored as typed, trimmed.
- **Update of existing people:** an existing email is skipped, never updated.
- **Upload content-type checks:** the file extension and part content type are not validated.

## Running

```
cd backend/CsvImportApi
dotnet run              # listens per launchSettings / ASPNETCORE_URLS
dotnet test ../CsvImportApi.Tests
```

Environment variables: none required. Standard ASP.NET Core settings (e.g. `ASPNETCORE_URLS`) apply.
