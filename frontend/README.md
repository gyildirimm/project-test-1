# Todo Frontend (vanilla JS)

Plain HTML/CSS/JS UI for the .NET 8 Todo API. No framework, build step, or npm packages.

## Run

1. Start the API (listens on `http://localhost:5000`):

   ```sh
   cd backend/TodoApi
   dotnet run
   ```

2. Open the UI, either:
   - directly: open `frontend/index.html` in a browser, or
   - via any static server, e.g. `cd frontend && python3 -m http.server 8080` → http://localhost:8080

If the API runs elsewhere, change `API_BASE` at the top of `app.js`.

## Features

- Lists todos on load
- Add a todo (Add button or Enter); blank input is ignored
- Toggle completion with the checkbox (completed items are struck through)
- Delete a todo
- Network failures and non-2xx responses show an error message above the list
