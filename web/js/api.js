// Thin fetch wrapper. Every call goes through request(), so error handling
// (400 field errors, 404/409 messages, 5xx and network failures) lives in one place.
import { statusLabel } from "./format.js";

const settings = window.EXPENSE_APP_CONFIG || {};
const API_BASE = String(settings.apiBase || "").replace(/\/+$/, "");

export class ApiError extends Error {
  constructor(message, { status = 0, fieldErrors = null } = {}) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    // Server's 400 `errors` object, e.g. { Title: ["..."] }. Null otherwise.
    this.fieldErrors = fieldErrors;
  }
}

export async function request(method, path, body) {
  const options = { method, headers: { Accept: "application/json" } };
  if (body !== undefined) {
    options.headers["Content-Type"] = "application/json";
    options.body = JSON.stringify(body);
  }

  let response;
  try {
    response = await fetch(API_BASE + path, options);
  } catch {
    throw new ApiError(
      "Sunucuya ulaşılamadı. API'nin çalıştığını ve config.js içindeki adresin doğru olduğunu kontrol edin."
    );
  }

  if (response.status === 204) return null;
  const data = await readJson(response);
  if (response.ok) return data;
  throw toApiError(response.status, data);
}

async function readJson(response) {
  const text = await response.text();
  if (!text) return null;
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}

function toApiError(status, data) {
  if (status === 400) {
    return new ApiError("Lütfen işaretli alanları düzeltip tekrar deneyin.", {
      status,
      fieldErrors: data?.errors ?? null,
    });
  }
  if (status === 404) {
    return new ApiError("Talep bulunamadı. Listeyi yenileyip tekrar deneyin.", { status });
  }
  if (status === 409) {
    return new ApiError(conflictMessage(data?.message), { status });
  }
  if (status >= 500) {
    return new ApiError(`Sunucu hatası oluştu (HTTP ${status}). Lütfen daha sonra tekrar deneyin.`, { status });
  }
  return new ApiError(`İstek başarısız oldu (HTTP ${status}).`, { status });
}

// Server texts: "Only Submitted expenses can be approved; this expense is Approved."
// and "Expense cannot be submitted from status Submitted."
function conflictMessage(serverMessage) {
  const match = /(?:this expense is|from status) (\w+)/i.exec(serverMessage ?? "");
  if (match) {
    return `Bu işlem talebin durumuyla uyuşmuyor (şu anki durum: ${statusLabel(match[1])}). Listeyi yenileyin.`;
  }
  return "Bu işlem talebin şu anki durumunda yapılamaz. Listeyi yenileyin.";
}

const enc = encodeURIComponent;

export const expenses = {
  list: (status) => request("GET", "/api/expenses" + (status ? `?status=${enc(status)}` : "")),
  get: (id) => request("GET", `/api/expenses/${enc(id)}`),
  create: (body) => request("POST", "/api/expenses", body),
  update: (id, body) => request("PUT", `/api/expenses/${enc(id)}`, body),
  remove: (id) => request("DELETE", `/api/expenses/${enc(id)}`),
  submit: (id) => request("POST", `/api/expenses/${enc(id)}/submit`),
};

export const approvals = {
  pending: () => request("GET", "/api/approvals/pending"),
  summary: () => request("GET", "/api/approvals/summary"),
  get: (id) => request("GET", `/api/approvals/${enc(id)}`),
  approve: (id) => request("POST", `/api/approvals/${enc(id)}/approve`, {}),
  reject: (id, reason) => request("POST", `/api/approvals/${enc(id)}/reject`, { reason }),
};
