# ExpenseApi

.NET 8 Minimal API with EF Core and SQLite. Backend foundation for draft expense management and submission.

Out of scope for this iteration: authentication, file upload, Docker, CI, deployment.

## Run and test

```bash
dotnet test
dotnet run --project src/ExpenseApi
```

## Configuration

| Setting | Env var | Default | Notes |
|---|---|---|---|
| `ConnectionStrings:Default` | `ConnectionStrings__Default` | `Data Source=expenses.db` (appsettings.json) | SQLite connection string. Relative paths resolve against the process working directory. The file and schema are created on startup if missing. |

Example: `ConnectionStrings__Default="Data Source=/var/data/expenses.db" dotnet run --project src/ExpenseApi`

## Data model

**Expense**

| Field | Type | Rules |
|---|---|---|
| `id` | GUID | server-generated |
| `title` | string | required, trimmed, max 200 |
| `description` | string? | optional, max 2000; empty becomes null |
| `amount` | decimal | required, > 0 (stored as decimal(18,2)) |
| `date` | date | required, `YYYY-MM-DD` |
| `status` | enum (stored and returned as string) | `Draft`, `Submitted`, `Approved`, `Rejected` |
| `decisionReason` | string? | max 2000; set by `POST /api/approvals/{id}/reject`. Null otherwise |
| `createdAt`, `updatedAt` | datetime with offset | server UTC, ISO 8601 (e.g. `2026-10-09T14:10:25.4776581+00:00`) |

**StatusHistory** (permanent, one row per status change; removed with its expense)

| Field | Type | Notes |
|---|---|---|
| `fromStatus` | enum or null | null for the creation entry |
| `toStatus` | enum | |
| `at` | datetime with offset | |
| `actor` / `role` | string | `employee` / `Employee` for expense actions, `manager` / `Manager` for approve/reject (no auth yet; constants in `StatusHistoryEntry` and `ApprovalEndpoints`). Not returned by the API. |
| `note` | string? | Optional on approve (`POST /api/approvals/{id}/approve`); always null for other transitions |

## Status transitions

- `Draft -> Submitted` (via `POST /submit`)
- `Submitted -> Approved` (via `POST /api/approvals/{id}/approve`)
- `Submitted -> Rejected` (via `POST /api/approvals/{id}/reject`, reason required)
- Approved and Rejected are final: any further approve/reject returns 409.
- Every other transition is invalid and returns 409.
- Only `Draft` expenses can be edited (PUT) or deleted (DELETE).

Creation writes a history entry `{fromStatus: null, toStatus: "Draft"}`.

## Endpoints

All bodies and responses are JSON (camelCase). Base path: `/api/expenses`.

### `POST /api/expenses`
Request: `{"title": string, "description"?: string|null, "amount": number, "date": "YYYY-MM-DD"}`

- `201 Created`, `Location: /api/expenses/{id}`, body = Expense (no `history`).
- `400` on validation errors.

### `GET /api/expenses`
Query: `status` (optional, exact name: `Draft|Submitted|Approved|Rejected`).

- `200` with a JSON array of Expense objects, newest `createdAt` first. No `history`.
- `400` `{"errors":{"Status":["Status must be one of: ..."]}}` for an unknown status.

### `GET /api/expenses/{id}`
- `200` with Expense + `history`.
- `404` if not found. A malformed GUID in the path also gives 404 (route constraint).

### `PUT /api/expenses/{id}`
Request: same as POST.

- `200` with Expense (no `history`).
- Check order: 404, then 409, then 400.
- `404` not found; `409` if status is not `Draft`; `400` validation.

### `DELETE /api/expenses/{id}`
- `204 No Content` on success.
- `404` not found; `409` if status is not `Draft`.

### `POST /api/expenses/{id}/submit`
No body.

- `200` with Expense + `history` (the new `Submitted` entry is last).
- `404` not found; `409` if the transition is not allowed (e.g. already Submitted).

### Expense object (response)
```json
{
  "id": "9c3c1b6a-3aea-488d-af7d-35e4d2b6fd5b",
  "title": "Taxi",
  "description": null,
  "amount": 12.5,
  "date": "2026-03-04",
  "status": "Draft",
  "createdAt": "2026-10-09T14:10:25.4776581+00:00",
  "updatedAt": "2026-10-09T14:10:25.4776581+00:00",
  "decisionReason": null
}
```

Detail and submit responses add a `history` array:
```json
"history": [
  {"fromStatus": null, "toStatus": "Draft", "at": "2026-10-09T14:10:25.4776581+00:00", "note": null},
  {"fromStatus": "Draft", "toStatus": "Submitted", "at": "2026-10-09T14:10:26.1883876+00:00", "note": null}
]
```

## Approvals

Base path: `/api/approvals`. Same JSON, error and history conventions as expenses. There is no authentication: every decision is recorded with actor `manager`, role `Manager`.

### `GET /api/approvals/pending`
- `200` with an array of Submitted expenses, oldest submission first:
  `[{"id": GUID, "title": string, "amount": number, "date": "YYYY-MM-DD", "submittedAt": timestamp}]`
- `submittedAt` is the timestamp of the most recent `Submitted` history entry.

### `GET /api/approvals/{id}`
- `200` with the same shape as `GET /api/expenses/{id}` (Expense + `history`, including `decisionReason`).
- `404` if not found.

### `POST /api/approvals/{id}/approve`
Body optional: `{"note"?: string|null}`. Empty body is allowed. Blank note becomes null; max 2000.

- `200` with Expense + `history` (the new `Approved` entry is last, with the note if one was given).
- `404` not found; `409` if the expense is not Submitted (`{"message": "Only Submitted expenses can be approved; this expense is Approved."}`); `400` if `note` is not a string or is too long.
- Check order: 404, 409, 400.

### `POST /api/approvals/{id}/reject`
Body: `{"reason": string}`. Required; trimmed; must not be blank; max 2000.

- `200` with Expense + `history` and `decisionReason` set to the trimmed reason.
- `404` not found; `409` if not Submitted; `400` `{"errors":{"Reason":["Reason is required."]}}` when the reason is missing, null, blank, or not a string.
- Check order: 404, 409, 400.

### `GET /api/approvals/summary`
```json
{
  "countsByStatus": {"Draft": 1, "Submitted": 2, "Approved": 3, "Rejected": 1},
  "approvedTotal": 101.00
}
```
- All four status keys are always present (0 when empty). `approvedTotal` is the sum of `amount` over Approved expenses; 0 when there are none.

## Error format

- **400 validation:** `{"errors": {"<Field>": ["message", ...]}}`. Field keys are PascalCase: `Title`, `Description`, `Amount`, `Date`, `Status`. A non-object body gives `{"errors":{"Body":[...]}}`. Wrong JSON types (e.g. `"amount": "10"`) are reported as field errors.
- **404:** `{"message": "Expense {id} was not found."}`
- **409:** `{"message": "..."}`

Malformed JSON syntax is rejected by ASP.NET before the handler runs and returns its default 400 body (not the format above).

## Deliberately left out

- Authentication/authorization; actor and role are fixed constants and not exposed.
- File attachments, Docker, CI, deployment.
- Notes on reject (the reason is stored in `decisionReason` instead).
- Locking against concurrent decisions on the same expense: the status check and save are not atomic (see Approvals).
- Pagination, sorting parameters, and filters other than `status`.
- EF Core migrations: the schema is created with `EnsureCreated()`. Before the first schema change to a deployed database, switch to migrations.
- Listing and approval summary sum amounts in memory, because SQLite cannot translate `decimal` aggregates. Fine for small data.
- Listing sorts in memory, because SQLite cannot order `DateTimeOffset` in SQL. Fine for small data; revisit if the list grows.
