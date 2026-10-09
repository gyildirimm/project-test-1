import { test, expect } from "@playwright/test";

const IMPORTS = "http://localhost:5000/api/customer-imports";
const CUSTOMERS = /localhost:5000\/api\/customers/;

const CSV = {
  name: "musteriler.csv",
  mimeType: "text/csv",
  buffer: Buffer.from("name,email,phone\nAda Lovelace,ada@example.com,+44 20 7946 0000\n"),
};

const PREVIEW = {
  totalRows: 4,
  importableCount: 1,
  invalidCount: 1,
  duplicateInFileCount: 1,
  alreadyExistsCount: 1,
  rows: [
    { lineNumber: 2, name: "Ada Lovelace", email: "ada@example.com", phone: "+44 20 7946 0000", status: "Importable", reason: null, duplicateOfLineNumber: null, customerId: null },
    { lineNumber: 3, name: "", email: "bad", phone: null, status: "Invalid", reason: "Name is required. Email must be a valid email address.", duplicateOfLineNumber: null, customerId: null },
    { lineNumber: 4, name: "Ada Lovelace", email: "ADA@example.com", phone: null, status: "DuplicateInFile", reason: "Same customer as line 2.", duplicateOfLineNumber: 2, customerId: null },
    { lineNumber: 5, name: "Grace Hopper", email: "grace@navy.example", phone: null, status: "AlreadyExists", reason: "A customer with this email already exists.", duplicateOfLineNumber: null, customerId: null },
  ],
};

const COMMITTED = {
  id: 7,
  fileName: "musteriler.csv",
  createdAt: "2026-10-09T12:00:00Z",
  totalRows: 4,
  importedCount: 1,
  invalidCount: 1,
  duplicateInFileCount: 1,
  alreadyExistsCount: 1,
  rows: PREVIEW.rows.map((row) =>
    row.status === "Importable" ? { ...row, status: "Imported", customerId: 3 } : row,
  ),
};

const HISTORY = [
  { id: 7, fileName: "musteriler.csv", createdAt: "2026-10-09T12:00:00Z", totalRows: 4, importedCount: 1, invalidCount: 1, duplicateInFileCount: 1, alreadyExistsCount: 1 },
  { id: 6, fileName: "eski.csv", createdAt: "2026-10-01T09:30:00Z", totalRows: 2, importedCount: 2, invalidCount: 0, duplicateInFileCount: 0, alreadyExistsCount: 0 },
];

// Stateful enough for the import flows: customers appear in the list after a commit.
// fail: { preview|commit|history|detail: { status, body } | "unreachable" }
async function mockImportApi(page, { history = HISTORY, preview = PREVIEW, commit = COMMITTED, details = {}, fail = {}, hold = null } = {}) {
  const calls = [];
  const customers = [];

  await page.route(CUSTOMERS, async (route) => {
    calls.push({ method: route.request().method(), path: new URL(route.request().url()).pathname });
    await route.fulfill({ status: 200, contentType: "application/json", headers: cors(), body: JSON.stringify(customers) });
  });

  await page.route(/localhost:5000\/api\/customer-imports/, async (route) => {
    const req = route.request();
    const url = new URL(req.url());
    const method = req.method();
    const path = url.pathname;
    calls.push({ method, path, body: req.postData() });

    const json = (status, payload, extraHeaders = {}) =>
      route.fulfill({ status, contentType: "application/json", headers: { ...cors(), ...extraHeaders }, body: JSON.stringify(payload) });

    if (method === "OPTIONS") return route.fulfill({ status: 204, headers: cors() });

    if (method === "POST" && path === "/api/customer-imports/preview") {
      if (fail.preview) return failWith(route, fail.preview, json);
      return json(200, preview);
    }

    if (method === "POST" && path === "/api/customer-imports") {
      if (hold) await hold;
      if (fail.commit) return failWith(route, fail.commit, json);
      for (const row of commit.rows) {
        if (row.status === "Imported") customers.push({ id: row.customerId, name: row.name, email: row.email, phone: row.phone });
      }
      return json(201, commit, { Location: `/api/customer-imports/${commit.id}` });
    }

    if (method === "GET" && path === "/api/customer-imports") {
      if (fail.history) return failWith(route, fail.history, json);
      return json(200, history);
    }

    const idMatch = path.match(/^\/api\/customer-imports\/(\d+)$/);
    if (method === "GET" && idMatch) {
      const id = Number(idMatch[1]);
      if (fail.detail) return failWith(route, fail.detail, json);
      if (details[id]) return json(200, details[id]);
      return json(404, { error: `Customer import ${id} was not found.` });
    }

    return json(405, { error: "Method not allowed." });
  });

  return { calls, fail };
}

function cors() {
  return { "Access-Control-Allow-Origin": "*", "Access-Control-Allow-Headers": "*", "Access-Control-Allow-Methods": "*" };
}

function failWith(route, failure, json) {
  if (failure === "unreachable") return route.abort("failed");
  return json(failure.status, failure.body);
}

async function openPage(page, mock) {
  await mock;
  await page.goto("/");
}

async function chooseFile(page, file = CSV) {
  await page.locator("#csv-file").setInputFiles(file);
}

function postsTo(calls, path) {
  return calls.filter((c) => c.method === "POST" && c.path === path);
}

test.describe("csv preview", () => {
  test("choosing a file shows totals and rows without saving anything", async ({ page }) => {
    const mock = await mockImportApi(page);
    await openPage(page, mock);

    await chooseFile(page);

    const preview = page.locator("#import-preview");
    await expect(preview).toBeVisible();
    await expect(page.locator("#preview-totals")).toContainText("Toplam satır");
    await expect(page.locator("#preview-totals dd").nth(0)).toHaveText("4");
    await expect(page.locator("#preview-totals dd").nth(1)).toHaveText("1");

    const rows = page.locator("#preview-table tbody tr");
    await expect(rows).toHaveCount(4);
    await expect(rows.nth(0)).toContainText("Eklenebilir");
    await expect(rows.nth(1)).toContainText("Geçersiz");
    await expect(rows.nth(1)).toContainText("Name is required.");
    await expect(rows.nth(2)).toContainText("Dosyada tekrar");
    await expect(rows.nth(3)).toContainText("Zaten var");

    const previewCall = mock.calls.find((c) => c.method === "POST");
    expect(previewCall.path).toBe("/api/customer-imports/preview");
    expect(previewCall.body).toContain("musteriler.csv");
    expect(postsTo(mock.calls, "/api/customer-imports")).toHaveLength(0);
  });

  test("shows the server error when the file is rejected", async ({ page }) => {
    const mock = await mockImportApi(page, {
      fail: { preview: { status: 400, body: { error: "The CSV file must have a name column." } } },
    });
    await openPage(page, mock);

    await chooseFile(page);

    await expect(page.locator("#import-error")).toHaveText("The CSV file must have a name column.");
    await expect(page.locator("#import-preview")).toBeHidden();
    expect(postsTo(mock.calls, "/api/customer-imports")).toHaveLength(0);
  });

  test("keeps the earlier preview when a new file fails to preview", async ({ page }) => {
    const mock = await mockImportApi(page);
    await openPage(page, mock);
    await chooseFile(page);
    await expect(page.locator("#import-preview")).toBeVisible();

    mock.fail.preview = "unreachable";
    await chooseFile(page, { name: "broken.csv", mimeType: "text/csv", buffer: Buffer.from("x") });

    await expect(page.locator("#import-error")).toContainText("Could not reach the API");
    await expect(page.locator("#import-preview")).toBeVisible();
    await expect(page.locator("#preview-file")).toHaveText("Dosya: musteriler.csv");
    await expect(page.getByRole("button", { name: "İçe aktar" })).toBeEnabled();
  });
});

test.describe("csv confirm", () => {
  test("confirm posts the same file, shows the result and refreshes the customer list", async ({ page }) => {
    const mock = await mockImportApi(page);
    await openPage(page, mock);
    await chooseFile(page);
    await expect(page.locator("#import-preview")).toBeVisible();
    const customerGetsBefore = mock.calls.filter((c) => c.path === "/api/customers").length;

    await page.getByRole("button", { name: "İçe aktar" }).click();

    await expect(page.locator("#import-result")).toHaveText("1 müşteri eklendi.");
    await expect(page.locator("#import-preview")).toBeHidden();
    await expect(page.locator("#customer-rows tr")).toHaveCount(1);
    await expect(page.locator("#customer-rows")).toContainText("Ada Lovelace");
    expect(mock.calls.filter((c) => c.path === "/api/customers").length).toBeGreaterThan(customerGetsBefore);

    const commitCall = postsTo(mock.calls, "/api/customer-imports")[0];
    expect(commitCall.body).toContain("musteriler.csv");
    expect(commitCall.body).toContain("Ada Lovelace");
    await expect(page.locator("#detail-title")).toContainText("musteriler.csv");
  });

  test("disables confirm while the request is in flight so it cannot be submitted twice", async ({ page }) => {
    let release;
    const hold = new Promise((resolve) => (release = resolve));
    const mock = await mockImportApi(page, { hold });
    await openPage(page, mock);
    await chooseFile(page);

    const confirm = page.getByRole("button", { name: "İçe aktar" });
    await confirm.click();
    await expect(confirm).toBeDisabled();
    await expect(page.getByRole("button", { name: "İptal" })).toBeDisabled();

    release();
    await expect(page.locator("#import-result")).toHaveText("1 müşteri eklendi.");
    expect(postsTo(mock.calls, "/api/customer-imports")).toHaveLength(1);
  });

  test("shows the server error and keeps the preview when the commit is rejected", async ({ page }) => {
    const mock = await mockImportApi(page, {
      fail: { commit: { status: 400, body: { error: "The CSV file is empty." } } },
    });
    await openPage(page, mock);
    await chooseFile(page);

    await page.getByRole("button", { name: "İçe aktar" }).click();

    await expect(page.locator("#import-error")).toHaveText("The CSV file is empty.");
    await expect(page.locator("#import-preview")).toBeVisible();
    await expect(page.getByRole("button", { name: "İçe aktar" })).toBeEnabled();
  });

  test("keeps the preview on a network failure so the user can retry", async ({ page }) => {
    const mock = await mockImportApi(page, { fail: { commit: "unreachable" } });
    await openPage(page, mock);
    await chooseFile(page);

    await page.getByRole("button", { name: "İçe aktar" }).click();

    await expect(page.locator("#import-error")).toContainText("Could not reach the API at http://localhost:5000/api/customer-imports");
    await expect(page.locator("#import-preview")).toBeVisible();
    await expect(page.getByRole("button", { name: "İçe aktar" })).toBeEnabled();
    await expect(page.locator("#import-result")).toBeHidden();
  });
});

test.describe("csv cancel", () => {
  test("cancel discards the preview without sending a commit", async ({ page }) => {
    const mock = await mockImportApi(page);
    await openPage(page, mock);
    await chooseFile(page);
    await expect(page.locator("#import-preview")).toBeVisible();

    await page.getByRole("button", { name: "İptal" }).click();

    await expect(page.locator("#import-preview")).toBeHidden();
    await expect(page.locator("#csv-file")).toHaveValue("");
    expect(postsTo(mock.calls, "/api/customer-imports")).toHaveLength(0);
    expect(mock.calls.filter((c) => c.method === "POST")).toHaveLength(1);
  });
});

test.describe("import history", () => {
  test("lists past imports newest first with their counts", async ({ page }) => {
    const mock = await mockImportApi(page);
    await openPage(page, mock);

    const items = page.locator("#history-list li");
    await expect(items).toHaveCount(2);
    await expect(items.nth(0)).toContainText("musteriler.csv");
    await expect(items.nth(0)).toContainText("4 satır");
    await expect(items.nth(0)).toContainText("1 eklendi");
    await expect(items.nth(0)).toContainText("2 tekrar");
    await expect(items.nth(1)).toContainText("eski.csv");
  });

  test("shows the empty state when nothing was imported yet", async ({ page }) => {
    await openPage(page, mockImportApi(page, { history: [] }));

    await expect(page.locator("#history-status")).toHaveText("Henüz içe aktarma yapılmadı.");
    await expect(page.locator("#history-list")).toBeHidden();
  });

  test("selecting an entry shows its summary, invalid rows and duplicate rows", async ({ page }) => {
    const mock = await mockImportApi(page, { details: { 7: COMMITTED } });
    await openPage(page, mock);

    await page.locator("#history-list li").nth(0).getByRole("button").click();

    await expect(page.locator("#import-detail")).toBeVisible();
    await expect(page.locator("#detail-title")).toContainText("musteriler.csv");
    await expect(page.locator("#detail-totals")).toContainText("Eklenen");

    const errors = page.locator("#detail-errors tbody tr");
    await expect(errors).toHaveCount(1);
    await expect(errors.first()).toContainText("Name is required.");

    const duplicates = page.locator("#detail-duplicates tbody tr");
    await expect(duplicates).toHaveCount(2);
    await expect(duplicates.nth(0)).toContainText("Dosyada tekrar");
    await expect(duplicates.nth(1)).toContainText("Zaten var");

    expect(mock.calls.some((c) => c.method === "GET" && c.path === "/api/customer-imports/7")).toBe(true);
    await expect(page.locator("#history-list li").nth(0).getByRole("button")).toHaveAttribute("aria-pressed", "true");
  });

  test("shows a message when the history cannot be loaded", async ({ page }) => {
    await openPage(page, mockImportApi(page, { fail: { history: "unreachable" } }));

    await expect(page.locator("#history-status")).toContainText("İçe aktarma geçmişi yüklenemedi.");
    await expect(page.getByRole("alert")).toHaveCount(0);
  });

  test("shows file names and cells as plain text, not HTML", async ({ page }) => {
    const hostile = [{ ...HISTORY[0], id: 9, fileName: "<img src=x onerror=\"window.__xss=1\">.csv" }];
    await openPage(page, mockImportApi(page, { history: hostile }));

    await expect(page.locator("#history-list strong")).toHaveText("<img src=x onerror=\"window.__xss=1\">.csv");
    expect(await page.evaluate(() => window.__xss)).toBeUndefined();
  });
});
