// Read-only expense details (fields, decision reason, status history).
// Shared by the employee and manager views.
import { el } from "./dom.js";
import { statusLabel, formatAmount, formatDateTime } from "./format.js";

export function renderExpenseDetails(container, expense) {
  const rows = [
    ["Başlık", expense.title],
    ["Açıklama", expense.description || "—"],
    ["Tutar (TL)", formatAmount(expense.amount)],
    ["Tarih", expense.date],
    ["Durum", statusLabel(expense.status)],
  ];
  if (expense.status === "Rejected") {
    rows.push(["Red gerekçesi", expense.decisionReason || "—"]);
  }

  const definitions = el(
    "dl",
    { className: "details" },
    rows.flatMap(([term, value]) => [el("dt", { text: term }), el("dd", { text: String(value) })])
  );

  const history = expense.history ?? [];
  const historyItems = history.map((entry) => {
    const change = entry.fromStatus
      ? `${statusLabel(entry.fromStatus)} → ${statusLabel(entry.toStatus)}`
      : `Oluşturuldu: ${statusLabel(entry.toStatus)}`;
    const children = [
      el("span", { className: "history-change", text: change }),
      el("span", { className: "muted", text: formatDateTime(entry.at) }),
    ];
    if (entry.note) children.push(el("span", { text: `Not: ${entry.note}` }));
    return el("li", {}, children);
  });

  container.replaceChildren(
    definitions,
    el("h4", { text: "Geçmiş" }),
    history.length
      ? el("ol", { className: "history" }, historyItems)
      : el("p", { className: "muted", text: "Kayıt yok." })
  );
}
