// Set this to wherever the customer API runs (see README).
const API_BASE = "http://localhost:5000/api/customers";

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

async function request(path, options = {}) {
  let res;
  try {
    res = await fetch(API_BASE + path, {
      ...options,
      headers: options.body ? { "Content-Type": "application/json" } : undefined,
    });
  } catch {
    throw new ApiError("Could not reach the API at " + API_BASE + ". Is it running?");
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

loadCustomers();
