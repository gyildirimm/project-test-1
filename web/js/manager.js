// Yönetici görünümü: summary cards, pending list, review detail, approve / reject.
import { approvals, ApiError } from "./api.js";
import { STATUS_OPTIONS, statusLabel, formatAmount, formatDateTime } from "./format.js";
import {
  el,
  setMessage,
  clearFieldErrors,
  showFieldError,
  showServerFieldErrors,
  guard,
} from "./dom.js";
import { renderExpenseDetails } from "./details.js";

const summaryMessage = document.getElementById("summary-message");
const summaryBox = document.getElementById("summary");
const refreshButton = document.getElementById("refresh-manager");
const pendingStatus = document.getElementById("pending-status");
const pendingList = document.getElementById("pending-list");
const detailSection = document.getElementById("manager-detail");
const detailBody = document.getElementById("manager-detail-body");
const detailMessage = document.getElementById("manager-detail-message");
const closeDetailButton = document.getElementById("close-manager-detail");
const decisionArea = document.getElementById("decision-area");
const approveButton = document.getElementById("approve-button");
const rejectForm = document.getElementById("reject-form");
const rejectReason = document.getElementById("reject-reason");
const rejectButton = document.getElementById("reject-button");

let selectedId = null;

export function initManagerView() {
  refreshButton.addEventListener("click", () => refreshList());
  pendingList.addEventListener("click", (event) => {
    const button = event.target.closest("button[data-action='review']");
    if (button) showReview(button.closest("[data-id]").dataset.id);
  });
  closeDetailButton.addEventListener("click", closeReview);
  approveButton.addEventListener("click", () => runDecision(approve));
  rejectForm.addEventListener("submit", (event) => {
    event.preventDefault();
    rejectSelected();
  });
}

export function refreshManagerView() {
  return refreshList();
}

// Approve and reject share one guard, so only one decision request runs at a time.
const runDecision = guard(() => [approveButton, rejectButton], (action) => action());

function refreshList() {
  return Promise.all([loadSummary(), loadPending()]);
}

// ----- Summary -----

async function loadSummary() {
  setMessage(summaryMessage);
  try {
    const summary = await approvals.summary();
    summaryBox.replaceChildren(
      ...STATUS_OPTIONS.map((status) => statCard(statusLabel(status), summary.countsByStatus[status] ?? 0)),
      statCard("Onaylanan toplam (TL)", formatAmount(summary.approvedTotal))
    );
  } catch (error) {
    summaryBox.replaceChildren();
    setMessage(summaryMessage, error.message, "error");
  }
}

function statCard(label, value) {
  return el("div", { className: "stat" }, [
    el("span", { className: "stat-label", text: label }),
    el("strong", { className: "stat-value", text: String(value) }),
  ]);
}

// ----- Pending list -----

async function loadPending() {
  pendingList.replaceChildren();
  setMessage(pendingStatus, "Yükleniyor…", "loading");
  try {
    const items = await approvals.pending();
    setMessage(pendingStatus, items.length ? "" : "Bekleyen talep yok.");
    pendingList.replaceChildren(...items.map(renderPendingItem));
  } catch (error) {
    setMessage(pendingStatus, error.message, "error");
  }
}

function renderPendingItem(item) {
  return el("li", { className: "item", attrs: { "data-id": item.id } }, [
    el("div", { className: "item-main" }, [
      el("strong", { text: item.title }),
      el("span", { className: "muted", text: `${formatAmount(item.amount)} TL · ${item.date}` }),
      el("span", { className: "muted", text: `Gönderildi: ${formatDateTime(item.submittedAt)}` }),
    ]),
    el("div", { className: "item-actions" }, [
      el("button", { text: "İncele", attrs: { type: "button", "data-action": "review" } }),
    ]),
  ]);
}

// ----- Review detail -----

async function showReview(id, flash = null) {
  selectedId = id;
  detailSection.hidden = false;
  detailBody.replaceChildren(el("p", { className: "muted", text: "Yükleniyor…" }));
  decisionArea.hidden = true;
  setMessage(detailMessage);
  rejectForm.reset();
  clearFieldErrors(rejectForm);

  try {
    const expense = await approvals.get(id);
    renderExpenseDetails(detailBody, expense);
    const open = expense.status === "Submitted";
    decisionArea.hidden = !open;
    if (flash) {
      setMessage(detailMessage, flash, "success");
    } else if (!open) {
      setMessage(detailMessage, `Bu talep "${statusLabel(expense.status)}" durumunda; artık işlem yapılamaz.`);
    }
  } catch (error) {
    closeReview();
    setMessage(pendingStatus, error.message, "error");
  }
}

function closeReview() {
  selectedId = null;
  detailSection.hidden = true;
  detailBody.replaceChildren();
  decisionArea.hidden = true;
}

// Runs an approve/reject request, then reloads the list, summary and the open review.
async function decide(request, successText) {
  const id = selectedId;
  if (!id) return;
  try {
    await request(id);
    await refreshList();
    await showReview(id, successText);
  } catch (error) {
    if (!(error instanceof ApiError)) throw error;
    await refreshList();
    if (selectedId === id) {
      await showReview(id);
      setMessage(detailMessage, error.message, "error");
      if (error.fieldErrors) showServerFieldErrors(rejectForm, error.fieldErrors);
    }
  }
}

function approve() {
  return decide((id) => approvals.approve(id), "Talep onaylandı.");
}

// Reason is required on the client too; the server still validates it.
function rejectSelected() {
  clearFieldErrors(rejectForm);
  const reason = rejectReason.value.trim();
  if (!reason) {
    showFieldError(rejectForm, "reason", "Red gerekçesi zorunludur.");
    rejectReason.focus();
    return;
  }
  runDecision(() =>
    decide((id) => approvals.reject(id, reason), "Talep reddedildi.")
  );
}
