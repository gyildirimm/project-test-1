const API_BASE = "http://localhost:5000/api/todos";

const form = document.getElementById("todo-form");
const input = document.getElementById("todo-input");
const list = document.getElementById("todo-list");
const errorEl = document.getElementById("error");
const statusEl = document.getElementById("status");

let todos = [];

// --- API ---------------------------------------------------------------

async function request(url, options = {}) {
  let res;
  try {
    res = await fetch(url, {
      ...options,
      headers: options.body ? { "Content-Type": "application/json" } : undefined,
    });
  } catch {
    throw new Error("Could not reach the API. Is it running at " + API_BASE + "?");
  }

  if (!res.ok) {
    throw new Error(await describeError(res));
  }
  return res.status === 204 ? null : res.json();
}

async function describeError(res) {
  // 400s from the API are ValidationProblem JSON: { errors: { title: ["..."] } }
  try {
    const body = await res.json();
    if (body && body.errors) {
      const messages = Object.values(body.errors).flat();
      if (messages.length) return messages.join(" ");
    }
    if (body && body.title) return body.title;
  } catch {
    // empty or non-JSON body
  }
  if (res.status === 404) return "Todo not found (it may have been deleted).";
  return "Request failed (" + res.status + ").";
}

const api = {
  list: () => request(API_BASE),
  create: (title) => request(API_BASE, { method: "POST", body: JSON.stringify({ title }) }),
  update: (todo) =>
    request(API_BASE + "/" + todo.id, {
      method: "PUT",
      body: JSON.stringify({ title: todo.title, isCompleted: todo.isCompleted }),
    }),
  remove: (id) => request(API_BASE + "/" + id, { method: "DELETE" }),
};

// --- UI ----------------------------------------------------------------

function showError(message) {
  errorEl.textContent = message;
  errorEl.hidden = false;
}

function clearError() {
  errorEl.textContent = "";
  errorEl.hidden = true;
}

function render() {
  list.replaceChildren(...todos.map(renderItem));
  statusEl.textContent = todos.length ? "" : "No todos yet.";
  statusEl.hidden = todos.length > 0;
}

function renderItem(todo) {
  const li = document.createElement("li");
  li.className = "todo-item" + (todo.isCompleted ? " completed" : "");

  const label = document.createElement("label");

  const checkbox = document.createElement("input");
  checkbox.type = "checkbox";
  checkbox.checked = todo.isCompleted;
  checkbox.addEventListener("change", () => toggleTodo(todo, checkbox));

  const title = document.createElement("span");
  title.className = "todo-title";
  title.textContent = todo.title;

  label.append(checkbox, title);

  const del = document.createElement("button");
  del.type = "button";
  del.className = "delete";
  del.textContent = "Delete";
  del.setAttribute("aria-label", "Delete “" + todo.title + "”");
  del.addEventListener("click", () => deleteTodo(todo, del));

  li.append(label, del);
  return li;
}

// --- Actions -----------------------------------------------------------

async function loadTodos() {
  try {
    todos = await api.list();
    clearError();
    render();
  } catch (err) {
    statusEl.textContent = "";
    showError(err.message);
  }
}

async function addTodo(event) {
  event.preventDefault();
  const title = input.value.trim();
  if (!title) {
    input.value = "";
    input.focus();
    return;
  }

  const button = form.querySelector("button");
  button.disabled = true;
  try {
    const created = await api.create(title);
    todos.push(created);
    input.value = "";
    clearError();
    render();
  } catch (err) {
    showError(err.message);
  } finally {
    button.disabled = false;
    input.focus();
  }
}

async function toggleTodo(todo, checkbox) {
  checkbox.disabled = true;
  try {
    const updated = await api.update({ ...todo, isCompleted: checkbox.checked });
    todos = todos.map((t) => (t.id === updated.id ? updated : t));
    clearError();
    render();
  } catch (err) {
    checkbox.checked = todo.isCompleted;
    checkbox.disabled = false;
    showError(err.message);
  }
}

async function deleteTodo(todo, button) {
  button.disabled = true;
  try {
    await api.remove(todo.id);
    todos = todos.filter((t) => t.id !== todo.id);
    clearError();
    render();
  } catch (err) {
    button.disabled = false;
    showError(err.message);
  }
}

form.addEventListener("submit", addTodo);
loadTodos();
