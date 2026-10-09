// Turkish labels and value formatting shared by both views.

export const STATUS_OPTIONS = ["Draft", "Submitted", "Approved", "Rejected"];

const STATUS_LABELS = {
  Draft: "Taslak",
  Submitted: "Onay bekliyor",
  Approved: "Onaylandı",
  Rejected: "Reddedildi",
};

export function statusLabel(status) {
  return STATUS_LABELS[status] ?? status ?? "—";
}

// 1234.5 -> "1.234,50"
export function formatAmount(value) {
  return Number(value).toLocaleString("tr-TR", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });
}

export function formatDateTime(iso) {
  if (!iso) return "—";
  return new Date(iso).toLocaleString("tr-TR", { dateStyle: "short", timeStyle: "short" });
}

// Today's local date as YYYY-MM-DD, the format the API expects.
export function todayIso() {
  const now = new Date();
  const local = new Date(now.getTime() - now.getTimezoneOffset() * 60000);
  return local.toISOString().slice(0, 10);
}
