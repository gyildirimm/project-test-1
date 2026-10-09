// Set this to wherever the customer API runs (see README).
const API_BASE = "http://localhost:5000/api/customers";
// CSV import endpoints sit next to the customer endpoints.
const IMPORT_BASE = API_BASE.replace(/\/customers$/, "/customer-imports");

// Same rule as the API, so the browser rejects what the server would reject.
const EMAIL_PATTERN = /^[^@\s]+@[^@\s]+\.[^@\s]+$/;

const form = document.getElementById("customer-form");
const formTitle = document.getElementById("form-title");
const submitButton = document.getElementById("submit-button");
const cancelButton = document.getElementById("cancel-button");
const fields = {
  name: document.getElementById("name"),
  email: document.getElementById("email"),
  phone: document.getElementById("phone"),
};
const searchInput = document.getElementById("search");
const statusEl = document.getElementById("status");
const table = document.getElementById("customer-table");
const rows = document.getElementById("customer-rows");
const errorEl = document.getElementById("error");

let customers = [];
let editingId = null;
let latestListRequest = 0;

// --- API ---------------------------------------------------------------

class ApiError extends Error {
  constructor(message, fieldErrors = null) {
    super(message);
    this.fieldErrors = fieldErrors;
  }
}

async function request(path, options = {}, base = API_BASE) {
  let res;
  try {
    res = await fetch(base + path, {
      ...options,
      // Only JSON bodies get a Content-Type; FormData sets its own multipart boundary.
      headers: typeof options.body === "string" ? { "Content-Type": "application/json" } : undefined,
    });
  } catch {
    throw new ApiError("Could not reach the API at " + base + ". Is it running?");
  }

  if (!res.ok) {
    throw await describeError(res);
  }
  return res.status === 204 ? null : res.json();
}

async function describeError(res) {
  let body = null;
  try {
    body = await res.json();
  } catch {
    // Empty or non-JSON body; fall back to the status code below.
  }

  if (res.status === 400 && body && body.errors) {
    // The API sends a list of messages per field; the form shows one line per field.
    const fieldErrors = Object.fromEntries(
      Object.entries(body.errors).map(([name, messages]) => [name, [].concat(messages).join(" ")]),
    );
    return new ApiError(body.error || "Please fix the highlighted fields.", fieldErrors);
  }
  if (body && body.error) {
    return new ApiError(body.error);
  }
  return new ApiError("Request failed (" + res.status + ").");
}

const api = {
  list: (search) => request("?search=" + encodeURIComponent(search)),
  create: (values) => request("", { method: "POST", body: JSON.stringify(values) }),
  update: (id, values) => request("/" + id, { method: "PUT", body: JSON.stringify(values) }),
  remove: (id) => request("/" + id, { method: "DELETE" }),
};

function fileForm(file) {
  const body = new FormData();
  body.append("file", file, file.name);
  return body;
}

const importApi = {
  list: () => request("", {}, IMPORT_BASE),
  get: (id) => request("/" + id, {}, IMPORT_BASE),
  preview: (file) => request("/preview", { method: "POST", body: fileForm(file) }, IMPORT_BASE),
  commit: (file) => request("", { method: "POST", body: fileForm(file) }, IMPORT_BASE),
};

// --- Messages ----------------------------------------------------------

function showError(message) {
  errorEl.textContent = message;
  errorEl.hidden = false;
}

function clearError() {
  errorEl.textContent = "";
  errorEl.hidden = true;
}

function setFieldError(name, message) {
  const field = fields[name];
  const errorP = document.getElementById(name + "-error");
  errorP.textContent = message || "";
  errorP.hidden = !message;
  field.setAttribute("aria-invalid", message ? "true" : "false");
}

function clearFieldErrors() {
  for (const name of Object.keys(fields)) {
    setFieldError(name, "");
  }
}

// --- Form --------------------------------------------------------------

function readForm() {
  return {
    name: fields.name.value.trim(),
    email: fields.email.value.trim() || null,
    phone: fields.phone.value.trim() || null,
  };
}

function validate(values) {
  const errors = {};
  if (!values.name) {
    errors.name = "Name is required.";
  }
  if (values.email && !EMAIL_PATTERN.test(values.email)) {
    errors.email = "Enter a valid email address, such as name@example.com.";
  }
  return errors;
}

function showFieldErrors(errors) {
  clearFieldErrors();
  let first = null;
  for (const [name, message] of Object.entries(errors)) {
    if (!fields[name]) continue;
    setFieldError(name, message);
    first = first || fields[name];
  }
  if (first) first.focus();
}

function resetForm() {
  form.reset();
  clearFieldErrors();
  editingId = null;
  formTitle.textContent = "Add customer";
  submitButton.textContent = "Add customer";
  cancelButton.hidden = true;
}

function startEdit(customer) {
  clearFieldErrors();
  editingId = customer.id;
  fields.name.value = customer.name;
  fields.email.value = customer.email || "";
  fields.phone.value = customer.phone || "";
  formTitle.textContent = "Edit customer";
  submitButton.textContent = "Save changes";
  cancelButton.hidden = false;
  fields.name.focus();
}

async function submitForm(event) {
  event.preventDefault();
  clearError();

  const values = readForm();
  const errors = validate(values);
  if (Object.keys(errors).length) {
    showFieldErrors(errors);
    return;
  }

  submitButton.disabled = true;
  try {
    if (editingId === null) {
      await api.create(values);
    } else {
      await api.update(editingId, values);
    }
    resetForm();
    await loadCustomers();
  } catch (err) {
    // Keep what the user typed so they can fix it and retry.
    if (err.fieldErrors) {
      showFieldErrors(err.fieldErrors);
    }
    showError(err.message);
  } finally {
    submitButton.disabled = false;
  }
}

// --- List --------------------------------------------------------------

function renderList() {
  rows.replaceChildren(...customers.map(renderRow));
  table.hidden = customers.length === 0;

  if (customers.length > 0) {
    statusEl.textContent = "";
  } else if (searchInput.value.trim()) {
    statusEl.textContent = "No customers match “" + searchInput.value.trim() + "”.";
  } else {
    statusEl.textContent = "No customers yet. Add your first customer above.";
  }
  statusEl.hidden = customers.length > 0;
}

function renderRow(customer) {
  const tr = document.createElement("tr");

  const name = document.createElement("td");
  name.textContent = customer.name;

  const email = document.createElement("td");
  email.textContent = customer.email || "—";

  const phone = document.createElement("td");
  phone.textContent = customer.phone || "—";

  const actions = document.createElement("td");
  actions.className = "actions";

  const edit = document.createElement("button");
  edit.type = "button";
  edit.textContent = "Edit";
  edit.setAttribute("aria-label", "Edit " + customer.name);
  edit.addEventListener("click", () => startEdit(customer));

  const del = document.createElement("button");
  del.type = "button";
  del.className = "delete";
  del.textContent = "Delete";
  del.setAttribute("aria-label", "Delete " + customer.name);
  del.addEventListener("click", () => deleteCustomer(customer));

  actions.append(edit, del);
  tr.append(name, email, phone, actions);
  return tr;
}

async function loadCustomers() {
  const requestId = ++latestListRequest;
  const search = searchInput.value.trim();
  statusEl.textContent = "Loading customers…";
  statusEl.hidden = false;

  try {
    const result = await api.list(search);
    // Ignore responses from searches the user has already moved past.
    if (requestId !== latestListRequest) return;
    customers = result;
    clearError();
    renderList();
  } catch (err) {
    if (requestId !== latestListRequest) return;
    customers = [];
    rows.replaceChildren();
    table.hidden = true;
    statusEl.textContent = "Could not load customers.";
    statusEl.hidden = false;
    showError(err.message);
  }
}

async function deleteCustomer(customer) {
  if (!window.confirm("Delete " + customer.name + "? This cannot be undone.")) {
    return;
  }

  try {
    await api.remove(customer.id);
    if (editingId === customer.id) {
      resetForm();
    }
    clearError();
    await loadCustomers();
  } catch (err) {
    showError(err.message);
  }
}

// --- CSV import --------------------------------------------------------

const importInput = document.getElementById("csv-file");
const importError = document.getElementById("import-error");
const importResult = document.getElementById("import-result");
const previewForm = document.getElementById("import-preview");
const previewFile = document.getElementById("preview-file");
const previewTotals = document.getElementById("preview-totals");
const previewTable = document.getElementById("preview-table");
const confirmButton = document.getElementById("import-confirm");
const cancelImportButton = document.getElementById("import-cancel");
const historyStatus = document.getElementById("history-status");
const historyList = document.getElementById("history-list");
const detailSection = document.getElementById("import-detail");
const detailTitle = document.getElementById("detail-title");
const detailTotals = document.getElementById("detail-totals");
const detailErrors = document.getElementById("detail-errors");
const detailDuplicates = document.getElementById("detail-duplicates");

// Turkish labels for the statuses the API returns.
const STATUS_LABELS = {
  Importable: "Eklenebilir",
  Invalid: "Geçersiz",
  DuplicateInFile: "Dosyada tekrar",
  AlreadyExists: "Zaten var",
  Imported: "Eklendi",
};

const COLUMNS = {
  line: { label: "Satır", value: (row) => row.lineNumber },
  name: { label: "Ad", value: (row) => row.name },
  email: { label: "E-posta", value: (row) => row.email },
  phone: { label: "Telefon", value: (row) => row.phone },
  status: { label: "Durum", value: (row) => STATUS_LABELS[row.status] || row.status },
  reason: { label: "Neden", value: (row) => row.reason },
  original: { label: "Özgün satır", value: (row) => row.duplicateOfLineNumber },
};
const PREVIEW_COLUMNS = [COLUMNS.line, COLUMNS.name, COLUMNS.email, COLUMNS.phone, COLUMNS.status, COLUMNS.reason, COLUMNS.original];
const ERROR_COLUMNS = [COLUMNS.line, COLUMNS.name, COLUMNS.email, COLUMNS.phone, COLUMNS.reason];
const DUPLICATE_COLUMNS = [COLUMNS.line, COLUMNS.name, COLUMNS.email, COLUMNS.phone, COLUMNS.status, COLUMNS.original];

// The file of the preview on screen, and its preview result. Nothing is saved until confirm.
let pendingImport = null;
let importBusy = false;
let selectedImportId = null;
let historyItems = [];
let latestHistoryRequest = 0;
let latestDetailRequest = 0;

function showImportError(message) {
  importError.textContent = message;
  importError.hidden = false;
}

function clearImportError() {
  importError.textContent = "";
  importError.hidden = true;
}

function updateImportControls() {
  importInput.disabled = importBusy;
  confirmButton.disabled = importBusy || !pendingImport;
  cancelImportButton.disabled = importBusy;
  previewForm.hidden = !pendingImport;
}

function discardPreview() {
  pendingImport = null;
  importInput.value = "";
}

function formatDate(value) {
  return new Date(value).toLocaleString("tr-TR", { dateStyle: "short", timeStyle: "short" });
}

function note(text) {
  const p = document.createElement("p");
  p.className = "hint";
  p.textContent = text;
  return p;
}

// Every cell is set with textContent, so file contents are never parsed as HTML.
function buildRowsTable(rows, columns) {
  const table = document.createElement("table");
  table.className = "customer-table import-table";

  const headRow = document.createElement("tr");
  for (const column of columns) {
    const th = document.createElement("th");
    th.scope = "col";
    th.textContent = column.label;
    headRow.append(th);
  }
  const thead = document.createElement("thead");
  thead.append(headRow);

  const tbody = document.createElement("tbody");
  tbody.append(
    ...rows.map((row) => {
      const tr = document.createElement("tr");
      for (const column of columns) {
        const value = column.value(row);
        const td = document.createElement("td");
        td.textContent = value === null || value === undefined || value === "" ? "—" : String(value);
        tr.append(td);
      }
      return tr;
    }),
  );

  table.append(thead, tbody);
  return table;
}

function renderTotals(dl, items) {
  dl.replaceChildren(
    ...items.flatMap(([label, value]) => {
      const dt = document.createElement("dt");
      dt.textContent = label;
      const dd = document.createElement("dd");
      dd.textContent = String(value);
      return [dt, dd];
    }),
  );
}

function renderPreview() {
  const { file, preview } = pendingImport;
  previewFile.textContent = "Dosya: " + file.name;
  renderTotals(previewTotals, [
    ["Toplam satır", preview.totalRows],
    ["Eklenebilir", preview.importableCount],
    ["Geçersiz", preview.invalidCount],
    ["Dosyada tekrar", preview.duplicateInFileCount],
    ["Zaten var", preview.alreadyExistsCount],
  ]);
  previewTable.replaceChildren(buildRowsTable(preview.rows, PREVIEW_COLUMNS));
}

async function previewImport(file) {
  importBusy = true;
  clearImportError();
  importResult.hidden = true;
  updateImportControls();
  try {
    const preview = await importApi.preview(file);
    pendingImport = { file, preview };
    renderPreview();
  } catch (err) {
    // Nothing was saved. An earlier preview stays on screen so it can still be confirmed.
    importInput.value = "";
    showImportError(err.message);
  } finally {
    importBusy = false;
    updateImportControls();
  }
}

async function confirmImport(event) {
  event.preventDefault();
  if (importBusy || !pendingImport) return;

  importBusy = true;
  clearImportError();
  updateImportControls();
  try {
    const detail = await importApi.commit(pendingImport.file);
    discardPreview();
    importResult.textContent = detail.importedCount + " müşteri eklendi.";
    importResult.hidden = false;
    selectedImportId = detail.id;
    showDetail(detail);
    // Both lists are reloaded from the server so they reflect what was saved.
    loadCustomers();
    loadImportHistory();
  } catch (err) {
    // Keep the preview so the user can retry or cancel.
    showImportError(err.message);
  } finally {
    importBusy = false;
    updateImportControls();
  }
}

function cancelImport() {
  discardPreview();
  clearImportError();
  updateImportControls();
}

async function loadImportHistory() {
  const requestId = ++latestHistoryRequest;
  historyStatus.textContent = "Yükleniyor…";
  historyStatus.hidden = false;

  try {
    const imports = await importApi.list();
    if (requestId !== latestHistoryRequest) return;
    historyItems = imports;
    renderHistory();
  } catch (err) {
    if (requestId !== latestHistoryRequest) return;
    historyItems = [];
    historyList.replaceChildren();
    historyList.hidden = true;
    historyStatus.textContent = "İçe aktarma geçmişi yüklenemedi. " + err.message;
  }
}

function renderHistory() {
  historyList.replaceChildren(...historyItems.map(renderHistoryItem));
  historyList.hidden = historyItems.length === 0;
  historyStatus.textContent = historyItems.length ? "" : "Henüz içe aktarma yapılmadı.";
  historyStatus.hidden = historyItems.length > 0;
}

function renderHistoryItem(item) {
  const li = document.createElement("li");

  const button = document.createElement("button");
  button.type = "button";
  button.className = "history-item";
  button.setAttribute("aria-pressed", String(item.id === selectedImportId));
  button.addEventListener("click", () => showImportDetail(item.id));

  const name = document.createElement("strong");
  name.textContent = item.fileName;

  const meta = document.createElement("span");
  meta.className = "hint";
  meta.textContent =
    formatDate(item.createdAt) +
    " · " + item.totalRows + " satır" +
    " · " + item.importedCount + " eklendi" +
    " · " + item.invalidCount + " geçersiz" +
    " · " + (item.duplicateInFileCount + item.alreadyExistsCount) + " tekrar";

  button.append(name, meta);
  li.append(button);
  return li;
}

async function showImportDetail(id) {
  const requestId = ++latestDetailRequest;
  selectedImportId = id;
  renderHistory();
  clearImportError();

  try {
    const detail = await importApi.get(id);
    if (requestId !== latestDetailRequest) return;
    showDetail(detail);
  } catch (err) {
    if (requestId !== latestDetailRequest) return;
    detailSection.hidden = true;
    showImportError(err.message);
  }
}

function showDetail(detail) {
  detailTitle.textContent = detail.fileName + " · " + formatDate(detail.createdAt);
  renderTotals(detailTotals, [
    ["Toplam satır", detail.totalRows],
    ["Eklenen", detail.importedCount],
    ["Geçersiz", detail.invalidCount],
    ["Dosyada tekrar", detail.duplicateInFileCount],
    ["Zaten var", detail.alreadyExistsCount],
  ]);

  const invalid = detail.rows.filter((row) => row.status === "Invalid");
  const duplicates = detail.rows.filter((row) => row.status === "DuplicateInFile" || row.status === "AlreadyExists");
  detailErrors.replaceChildren(invalid.length ? buildRowsTable(invalid, ERROR_COLUMNS) : note("Geçersiz satır yok."));
  detailDuplicates.replaceChildren(
    duplicates.length ? buildRowsTable(duplicates, DUPLICATE_COLUMNS) : note("Tekrar eden satır yok."),
  );
  detailSection.hidden = false;
}

// --- Wiring ------------------------------------------------------------

let searchTimer = null;
searchInput.addEventListener("input", () => {
  clearTimeout(searchTimer);
  searchTimer = setTimeout(loadCustomers, 250);
});

form.addEventListener("submit", submitForm);
cancelButton.addEventListener("click", () => {
  clearError();
  resetForm();
});

importInput.addEventListener("change", () => {
  const file = importInput.files[0];
  if (file) previewImport(file);
});
previewForm.addEventListener("submit", confirmImport);
cancelImportButton.addEventListener("click", cancelImport);

loadCustomers();
loadImportHistory();
