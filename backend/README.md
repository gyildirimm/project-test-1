# Todo API

ASP.NET Core 8 Minimal API backed by SQLite (EF Core).

## Run

Requires the .NET 8 SDK.

```bash
cd backend/TodoApi
dotnet run
```

The API listens on http://localhost:5000. The SQLite database file `todos.db` is created automatically in the working directory on first run; data persists across restarts.

To use a different database file, override the connection string:

```bash
ConnectionStrings__Default="Data Source=/tmp/other.db" dotnet run
```

CORS allows any origin, method and header.

## Endpoints

| Method | Path              | Body                                    | Responses                     |
|--------|-------------------|-----------------------------------------|-------------------------------|
| GET    | `/api/todos`      | –                                       | 200 `Todo[]` (ordered by id)  |
| GET    | `/api/todos/{id}` | –                                       | 200 `Todo`, 404               |
| POST   | `/api/todos`      | `{ "title": string }`                   | 201 `Todo` + `Location`, 400  |
| PUT    | `/api/todos/{id}` | `{ "title": string, "isCompleted": bool }` | 200 `Todo`, 400, 404       |
| DELETE | `/api/todos/{id}` | –                                       | 204, 404                      |

`Todo`:

```json
{ "id": 1, "title": "Buy milk", "isCompleted": false, "createdAt": "2026-10-07T10:25:58.87Z" }
```

Titles are trimmed; empty/whitespace or longer than 200 characters returns 400 with a `ValidationProblem` body (`{ "errors": { "title": ["..."] }, ... }`).
