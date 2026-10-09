// Runtime configuration. apiBase is the origin the UI calls for /api/...
// Default "" = same origin as this page (serve web/ behind a reverse proxy that also routes /api to the API).
// Example for a different origin: apiBase: "http://localhost:5000".
// The API currently sends no CORS headers, so a different origin only works after CORS is added to the API.
window.EXPENSE_APP_CONFIG = {
  apiBase: "",
};
