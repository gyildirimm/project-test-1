import { test, expect } from "@playwright/test";

const API = "http://localhost:5000/api/customers";

// Stateful fake of the customer API, so add/edit/delete flows see their own changes.
// Options let a test simulate an unreachable API or a server-side error for one endpoint.
async function mockApi(page, { customers = [], holdList = null, fail = {} } = {}) {
  const store = customers.map((c, i) => ({
    id: i + 1,
    email: null,
    phone: null,
    createdAt: "2026-10-09T12:00:00Z",
    updatedAt: "2026-10-09T12:00:00Z",
    ...c,
  }));
  let nextId = store.length + 1;
  const calls = [];

  await page.route(/localhost:5000\/api\/customers/, async (route) => {
    const req = route.request();
    const url = new URL(req.url());
    const method = req.method();
    const idMatch = url.pathname.match(/^\/api\/customers\/(\d+)$/);
    const id = idMatch ? Number(idMatch[1]) : null;
    const body = req.postData() ? JSON.parse(req.postData()) : null;
    calls.push({ method, path: url.pathname, search: url.searchParams.get("search"), body });

    const json = (status, payload) =>
      route.fulfill({
        status,
        contentType: "application/json",
        headers: { "Access-Control-Allow-Origin": "*", "Access-Control-Allow-Headers": "*", "Access-Control-Allow-Methods": "*" },
        body: payload === undefined ? "" : JSON.stringify(payload),
      });

    if (method === "OPTIONS") return json(204);

    const failure = fail[method];
    if (failure === "unreachable") return route.abort("failed");
    if (failure) return json(failure.status, failure.body);

    if (method === "GET" && !id) {
      if (holdList) await holdList;
      const term = (url.searchParams.get("search") || "").trim().toLowerCase();
      const matches = store.filter(
        (c) => !term || c.name.toLowerCase().includes(term) || (c.email || "").toLowerCase().includes(term),
      );
      return json(200, matches);
    }

    if (method === "POST" && !id) {
      const created = { id: nextId++, ...normalize(body), createdAt: now(), updatedAt: now() };
      store.push(created);
      return json(201, created);
    }

    const index = store.findIndex((c) => c.id === id);
    if (index === -1) return json(404, { error: `Customer ${id} was not found.` });

    if (method === "PUT") {
      store[index] = { ...store[index], ...normalize(body), updatedAt: now() };
      return json(200, store[index]);
    }

    if (method === "DELETE") {
      store.splice(index, 1);
      return json(204);
    }

    return json(405, { error: "Method not allowed." });
  });

  return { store, calls };
}

function normalize(body) {
  return {
    name: body.name,
    email: body.email || null,
    phone: body.phone || null,
  };
}

function now() {
  return "2026-10-09T12:00:00Z";
}

const ada = { id: 1, name: "Ada Lovelace", email: "ada@example.com", phone: "+44 20 7946 0000" };
const grace = { id: 2, name: "Grace Hopper", email: "grace@navy.example", phone: null };

async function openPage(page, mock) {
  await mock;
  await page.goto("/");
}

test.describe("customer list", () => {
  test("shows name, email and phone for each customer", async ({ page }) => {
    await openPage(page, mockApi(page, { customers: [ada, grace] }));

    const rows = page.locator("#customer-rows tr");
    await expect(rows).toHaveCount(2);
    await expect(rows.nth(0)).toContainText("Ada Lovelace");
    await expect(rows.nth(0)).toContainText("ada@example.com");
    await expect(rows.nth(0)).toContainText("+44 20 7946 0000");
    await expect(rows.nth(1)).toContainText("Grace Hopper");
    await expect(rows.nth(1)).toContainText("—");
  });

  test("shows an empty-state message when there are no customers", async ({ page }) => {
    await openPage(page, mockApi(page));

    await expect(page.getByRole("status")).toHaveText("No customers yet. Add your first customer above.");
    await expect(page.locator("#customer-table")).toBeHidden();
  });

  test("shows a loading indicator until the list arrives", async ({ page }) => {
    let release;
    const hold = new Promise((resolve) => (release = resolve));
    await openPage(page, mockApi(page, { customers: [ada], holdList: hold }));

    await expect(page.getByRole("status")).toHaveText("Loading customers…");
    release();
    await expect(page.locator("#customer-rows tr")).toHaveCount(1);
  });
});

test.describe("search", () => {
  test("filters by name or email and sends the search term to the API", async ({ page }) => {
    const mock = await mockApi(page, { customers: [ada, grace] });
    await openPage(page, mock);

    await page.getByLabel("Search by name or email").fill("navy");
    await expect(page.locator("#customer-rows tr")).toHaveCount(1);
    await expect(page.locator("#customer-rows")).toContainText("Grace Hopper");

    await page.getByLabel("Search by name or email").fill("LOVE");
    await expect(page.locator("#customer-rows tr")).toHaveCount(1);
    await expect(page.locator("#customer-rows")).toContainText("Ada Lovelace");

    expect(mock.calls.some((c) => c.method === "GET" && c.search === "navy")).toBe(true);
  });

  test("shows a distinct message when the search has no matches", async ({ page }) => {
    await openPage(page, mockApi(page, { customers: [ada] }));

    await page.getByLabel("Search by name or email").fill("zzz");
    await expect(page.getByRole("status")).toHaveText("No customers match “zzz”.");
    await expect(page.getByText("No customers yet")).toHaveCount(0);
  });
});

test.describe("add", () => {
  test("adds a customer and shows it in the list", async ({ page }) => {
    const mock = await mockApi(page);
    await openPage(page, mock);

    await page.getByLabel(/^Name/).fill("  Ada Lovelace ");
    await page.getByLabel(/^Email/).fill("ada@example.com");
    await page.getByLabel(/^Phone/).fill("+44 20 7946 0000");
    await page.getByRole("button", { name: "Add customer" }).click();

    await expect(page.locator("#customer-rows tr")).toHaveCount(1);
    await expect(page.locator("#customer-rows")).toContainText("Ada Lovelace");
    await expect(page.getByLabel(/^Name/)).toHaveValue("");
    expect(mock.calls.find((c) => c.method === "POST").body).toEqual({
      name: "Ada Lovelace",
      email: "ada@example.com",
      phone: "+44 20 7946 0000",
    });
  });

  test("requires a name before submitting", async ({ page }) => {
    const mock = await mockApi(page);
    await openPage(page, mock);

    await page.getByLabel(/^Name/).fill("   ");
    await page.getByRole("button", { name: "Add customer" }).click();

    await expect(page.locator("#name-error")).toHaveText("Name is required.");
    await expect(page.getByLabel(/^Name/)).toHaveAttribute("aria-invalid", "true");
    expect(mock.calls.some((c) => c.method === "POST")).toBe(false);
  });

  test("rejects an invalid email before submitting", async ({ page }) => {
    const mock = await mockApi(page);
    await openPage(page, mock);

    await page.getByLabel(/^Name/).fill("Grace");
    await page.getByLabel(/^Email/).fill("grace@navy");
    await page.getByRole("button", { name: "Add customer" }).click();

    await expect(page.locator("#email-error")).toBeVisible();
    await expect(page.locator("#name-error")).toBeHidden();
    expect(mock.calls.some((c) => c.method === "POST")).toBe(false);

    await page.getByLabel(/^Email/).fill("grace@navy.example");
    await page.getByRole("button", { name: "Add customer" }).click();
    await expect(page.locator("#customer-rows")).toContainText("Grace");
    await expect(page.locator("#email-error")).toBeHidden();
  });

  test("keeps entered values when the API rejects the request", async ({ page }) => {
    await openPage(
      page,
      mockApi(page, {
        fail: { POST: { status: 400, body: { error: "Validation failed.", errors: { name: ["Name is required."] } } } },
      }),
    );

    await page.getByLabel(/^Name/).fill("Grace");
    await page.getByLabel(/^Email/).fill("grace@navy.example");
    await page.getByRole("button", { name: "Add customer" }).click();

    await expect(page.locator("#error")).toHaveText("Validation failed.");
    await expect(page.getByLabel(/^Name/)).toHaveValue("Grace");
    await expect(page.getByLabel(/^Email/)).toHaveValue("grace@navy.example");
  });
});

test.describe("edit", () => {
  test("loads a customer into the form and shows the saved change in the row", async ({ page }) => {
    const mock = await mockApi(page, { customers: [ada] });
    await openPage(page, mock);

    await page.getByRole("button", { name: "Edit Ada Lovelace" }).click();
    await expect(page.getByRole("heading", { name: "Edit customer" })).toBeVisible();
    await expect(page.getByLabel(/^Name/)).toHaveValue("Ada Lovelace");
    await expect(page.getByLabel(/^Phone/)).toHaveValue("+44 20 7946 0000");

    await page.getByLabel(/^Phone/).fill("");
    await page.getByRole("button", { name: "Save changes" }).click();

    await expect(page.locator("#customer-rows tr")).toHaveCount(1);
    await expect(page.locator("#customer-rows tr").first()).toContainText("Ada Lovelace");
    await expect(page.locator("#customer-rows tr").first()).toContainText("—");
    expect(mock.calls.find((c) => c.method === "PUT")).toMatchObject({ path: "/api/customers/1", body: { name: "Ada Lovelace", phone: null } });
    await expect(page.getByRole("heading", { name: "Add customer" })).toBeVisible();
  });

  test("cancel leaves edit mode without saving", async ({ page }) => {
    const mock = await mockApi(page, { customers: [ada] });
    await openPage(page, mock);

    await page.getByRole("button", { name: "Edit Ada Lovelace" }).click();
    await page.getByLabel(/^Name/).fill("Someone Else");
    await page.getByRole("button", { name: "Cancel" }).click();

    await expect(page.getByLabel(/^Name/)).toHaveValue("");
    await expect(page.getByRole("button", { name: "Cancel" })).toBeHidden();
    expect(mock.calls.some((c) => c.method === "PUT")).toBe(false);
  });

  test("validates the email while editing", async ({ page }) => {
    const mock = await mockApi(page, { customers: [ada] });
    await openPage(page, mock);

    await page.getByRole("button", { name: "Edit Ada Lovelace" }).click();
    await page.getByLabel(/^Email/).fill("not-an-email");
    await page.getByRole("button", { name: "Save changes" }).click();

    await expect(page.locator("#email-error")).toBeVisible();
    expect(mock.calls.some((c) => c.method === "PUT")).toBe(false);
  });
});

test.describe("delete", () => {
  test("removes the row after confirmation", async ({ page }) => {
    const mock = await mockApi(page, { customers: [ada, grace] });
    await openPage(page, mock);

    page.once("dialog", (dialog) => dialog.accept());
    await page.getByRole("button", { name: "Delete Ada Lovelace" }).click();

    await expect(page.locator("#customer-rows tr")).toHaveCount(1);
    await expect(page.locator("#customer-rows")).not.toContainText("Ada Lovelace");
    expect(mock.calls.find((c) => c.method === "DELETE")).toMatchObject({ path: "/api/customers/1" });
  });

  test("does nothing when the confirmation is dismissed", async ({ page }) => {
    const mock = await mockApi(page, { customers: [ada] });
    await openPage(page, mock);

    page.once("dialog", (dialog) => dialog.dismiss());
    await page.getByRole("button", { name: "Delete Ada Lovelace" }).click();

    await expect(page.locator("#customer-rows tr")).toHaveCount(1);
    expect(mock.calls.some((c) => c.method === "DELETE")).toBe(false);
  });

  test("shows the empty state after deleting the only customer", async ({ page }) => {
    await openPage(page, mockApi(page, { customers: [ada] }));

    page.once("dialog", (dialog) => dialog.accept());
    await page.getByRole("button", { name: "Delete Ada Lovelace" }).click();

    await expect(page.getByRole("status")).toHaveText("No customers yet. Add your first customer above.");
  });
});

test.describe("errors", () => {
  test("shows a clear message when the API is unreachable on load", async ({ page }) => {
    await openPage(page, mockApi(page, { fail: { GET: "unreachable" } }));

    await expect(page.getByRole("alert")).toContainText("Could not reach the API at http://localhost:5000/api/customers");
    await expect(page.getByRole("status")).toHaveText("Could not load customers.");
  });

  test("keeps form data when the API is unreachable on save", async ({ page }) => {
    await openPage(page, mockApi(page, { fail: { POST: "unreachable" } }));

    await page.getByLabel(/^Name/).fill("Grace Hopper");
    await page.getByLabel(/^Phone/).fill("555-0100");
    await page.getByRole("button", { name: "Add customer" }).click();

    await expect(page.getByRole("alert")).toContainText("Could not reach the API");
    await expect(page.getByLabel(/^Name/)).toHaveValue("Grace Hopper");
    await expect(page.getByLabel(/^Phone/)).toHaveValue("555-0100");
  });

  test("shows the server error when a delete fails", async ({ page }) => {
    await openPage(
      page,
      mockApi(page, { customers: [ada], fail: { DELETE: { status: 500, body: { error: "An unexpected error occurred." } } } }),
    );

    page.once("dialog", (dialog) => dialog.accept());
    await page.getByRole("button", { name: "Delete Ada Lovelace" }).click();

    await expect(page.getByRole("alert")).toHaveText("An unexpected error occurred.");
    await expect(page.locator("#customer-rows tr")).toHaveCount(1);
  });
});
