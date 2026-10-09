// Çalışan görünümü: draft form, expense list, submit / edit / delete, detail.
import { expenses, ApiError } from "./api.js";
import { STATUS_OPTIONS, statusLabel, formatAmount } from "./format.js";
import {
  el,
  actionButton,
  setMessage,
  clearFieldErrors,
  showFieldError,
  showServerFieldErrors,
  guard,
} from "./dom.js";
import { renderExpenseDetails } from "./details.js";

const form = document.getElementById("expense-form");
const formTitle = document.getElementById("form-title");
const formMessage = document.getElementById("form-message");
const saveButton = document.getElementById("save-button");
const cancelEditButton = document.getElementById("cancel-edit-button");
const statusFilter = document.getElementById("status-filter");
const refreshButton = document.getElementById("refresh-expenses");
const actionMessage = document.getElementById("action-message");
const listStatus = document.getElementById("expense-list-status");
const list = document.getElementById("expense-list");
const detailSection = document.getElementById("expense-detail");
const detailBody = document.getElementById("expense-detail-body");
const closeDetailButton = document.getElementById("close-expense-detail");

const MAX_TITLE = 200;
const MAX_DESCRIPTION = 2000;

let editingId = null;
let detailId = null;
// Last loaded list, keyed by id, so "Düzenle" can fill the form without a request.
const loaded = new Map();

export function initEmployeeView() {
  statusFilter.append(
    ...[["", "Tümü"], ...STATUS_OPTIONS.map((s) => [s, statusLabel(s)])].map(([value, text]) =>
      el("option", { text, attrs: { value } })
    )
  );

  form.addEventListener("submit", (event) => {
    event.preventDefault();
    saveDraft();
  });
  cancelEditButton.addEventListener("click", () => resetForm());
  statusFilter.addEventListener("change", () => loadExpenses());
  refreshButton.addEventListener("click", () => loadExpenses());
  list.addEventListener("click", runListAction);
  closeDetailButton.addEventListener("click", closeDetail);
}

export function refreshEmployeeView() {
  return loadExpenses();
}

// ----- Form -----

const saveDraft = guard([saveButton, cancelEditButton], async () => {
  clearFieldErrors(form);
  setMessage(formMessage);
  setMessage(actionMessage);

  const body = readForm();
  if (!validateForm(body)) return;

  try {
    if (editingId) {
      await expenses.update(editingId, body);
    } else {
      await expenses.create(body);
    }
    const message = editingId ? "Taslak güncellendi." : "Taslak oluşturuldu.";
    resetForm();
    setMessage(formMessage, message, "success");
    await loadExpenses();
  } catch (error) {
    if (!(error instanceof ApiError)) throw error;
    if (error.fieldErrors) showServerFieldErrors(form, error.fieldErrors);
    setMessage(formMessage, error.message, "error");
    if (error.status === 409) {
      // The draft may have been submitted elsewhere; leave edit mode and refresh.
      resetForm();
      await loadExpenses();
    }
  }
});

function readForm() {
  const data = new FormData(form);
  const amountText = String(data.get("amount")).trim();
  const description = String(data.get("description")).trim();
  return {
    title: String(data.get("title")).trim(),
    description: description || null,
    amount: amountText === "" ? null : Number(amountText),
    date: String(data.get("date")),
  };
}

// Client-side checks for obvious mistakes. The server remains the authority.
function validateForm(body) {
  let valid = true;
  const fail = (field, message) => {
    showFieldError(form, field, message);
    valid = false;
  };

  if (!body.title) fail("title", "Başlık zorunludur.");
  else if (body.title.length > MAX_TITLE) fail("title", `Başlık en fazla ${MAX_TITLE} karakter olabilir.`);

  if (body.description && body.description.length > MAX_DESCRIPTION) {
    fail("description", `Açıklama en fazla ${MAX_DESCRIPTION} karakter olabilir.`);
  }

  if (body.amount === null || !Number.isFinite(body.amount) || body.amount <= 0) {
    fail("amount", "Tutar 0'dan büyük bir sayı olmalıdır.");
  }

  if (!/^\d{4}-\d{2}-\d{2}$/.test(body.date)) {
    fail("date", "Lütfen bir tarih seçin.");
  }

  if (!valid) {
    setMessage(formMessage, "Lütfen işaretli alanları düzeltip tekrar deneyin.", "error");
    form.querySelector("[aria-invalid='true']")?.focus();
  }
  return valid;
}

function startEdit(expense) {
  editingId = expense.id;
  form.elements.title.value = expense.title;
  form.elements.description.value = expense.description ?? "";
  form.elements.amount.value = expense.amount;
  form.elements.date.value = expense.date;
  clearFieldErrors(form);
  setMessage(formMessage);
  formTitle.textContent = "Taslağı düzenle";
  saveButton.textContent = "Değişiklikleri kaydet";
  cancelEditButton.hidden = false;
  form.elements.title.focus();
}

// Clears the form and leaves edit mode. Does not touch the message areas.
function resetForm() {
  form.reset();
  editingId = null;
  clearFieldErrors(form);
  formTitle.textContent = "Yeni taslak";
  saveButton.textContent = "Taslağı kaydet";
  cancelEditButton.hidden = true;
}

// ----- List -----

async function loadExpenses() {
  const status = statusFilter.value;
  list.replaceChildren();
  setMessage(listStatus, "Yükleniyor…", "loading");
  try {
    const items = await expenses.list(status || undefined);
    loaded.clear();
    items.forEach((item) => loaded.set(item.id, item));
    if (items.length === 0) {
      setMessage(
        listStatus,
        status ? "Bu durumda talep yok." : "Henüz talep yok. Formdan bir taslak oluşturabilirsiniz."
      );
    } else {
      setMessage(listStatus);
    }
    list.replaceChildren(...items.map(renderItem));
  } catch (error) {
    setMessage(listStatus, error.message, "error");
  }
}

function renderItem(expense) {
  const actions = [actionButton("Ayrıntı", "detail")];
  // Only drafts can be edited, submitted or deleted.
  if (expense.status === "Draft") {
    actions.push(
      actionButton("Düzenle", "edit"),
      actionButton("Onaya gönder", "submit", "primary"),
      actionButton("Sil", "delete", "danger")
    );
  }
  return el("li", { className: "item", attrs: { "data-id": expense.id } }, [
    el("div", { className: "item-main" }, [
      el("strong", { text: expense.title }),
      el("span", { className: "muted", text: `${formatAmount(expense.amount)} TL · ${expense.date}` }),
    ]),
    el("span", {
      className: `badge badge-${expense.status.toLowerCase()}`,
      text: statusLabel(expense.status),
    }),
    el("div", { className: "item-actions" }, actions),
  ]);
}

// All list buttons are disabled while any list action runs.
const runListAction = guard(() => Array.from(list.querySelectorAll("button")), handleListClick);

async function handleListClick(event) {
  const button = event.target.closest("button[data-action]");
  if (!button) return;
  const id = button.closest("[data-id]").dataset.id;

  switch (button.dataset.action) {
    case "detail":
      return showDetail(id);
    case "edit":
      return startEdit(loaded.get(id));
    case "submit":
      return submitDraft(id);
    case "delete":
      return deleteDraft(id);
  }
}

async function submitDraft(id) {
  setMessage(actionMessage);
  try {
    await expenses.submit(id);
    closeIfShowing(id);
    await loadExpenses();
    setMessage(actionMessage, "Talep onaya gönderildi.", "success");
  } catch (error) {
    setMessage(actionMessage, error.message, "error");
    await loadExpenses();
  }
}

async function deleteDraft(id) {
  if (!window.confirm("Bu taslak silinsin mi? Bu işlem geri alınamaz.")) return;
  setMessage(actionMessage);
  try {
    await expenses.remove(id);
    if (editingId === id) resetForm();
    closeIfShowing(id);
    await loadExpenses();
    setMessage(actionMessage, "Taslak silindi.", "success");
  } catch (error) {
    setMessage(actionMessage, error.message, "error");
    await loadExpenses();
  }
}

// ----- Detail -----

async function showDetail(id) {
  detailId = id;
  detailSection.hidden = false;
  detailBody.replaceChildren(el("p", { className: "muted", text: "Yükleniyor…" }));
  try {
    const expense = await expenses.get(id);
    renderExpenseDetails(detailBody, expense);
  } catch (error) {
    closeDetail();
    setMessage(actionMessage, error.message, "error");
  }
}

function closeDetail() {
  detailId = null;
  detailSection.hidden = true;
  detailBody.replaceChildren();
}

function closeIfShowing(id) {
  if (detailId === id) closeDetail();
}
