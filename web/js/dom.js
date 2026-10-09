// Small DOM helpers. Text is always set with textContent, never innerHTML.

const FIELD_MESSAGE = "Bu alan geçersiz veya eksik.";

export function el(tag, options = {}, children = []) {
  const node = document.createElement(tag);
  if (options.className) node.className = options.className;
  if (options.text !== undefined) node.textContent = options.text;
  for (const [name, value] of Object.entries(options.attrs ?? {})) {
    node.setAttribute(name, value);
  }
  node.append(...children);
  return node;
}

export function actionButton(text, action, className = "") {
  return el("button", { text, className, attrs: { type: "button", "data-action": action } });
}

// kind: "info" | "success" | "error" | "loading". Empty text hides the element.
export function setMessage(node, text = "", kind = "info") {
  node.textContent = text;
  node.dataset.kind = kind;
  node.hidden = !text;
}

export function clearFieldErrors(root) {
  root.querySelectorAll("[data-error-for]").forEach((p) => {
    p.textContent = "";
    p.hidden = true;
  });
  root.querySelectorAll("[aria-invalid]").forEach((input) => input.removeAttribute("aria-invalid"));
}

export function showFieldError(root, field, message = FIELD_MESSAGE) {
  const input = root.querySelector(`[name="${field}"]`);
  const p = root.querySelector(`[data-error-for="${field}"]`);
  if (input) input.setAttribute("aria-invalid", "true");
  if (p) {
    p.textContent = message;
    p.hidden = false;
  }
}

// Maps the server's { Title: [...] } keys to form field names (title).
export function showServerFieldErrors(root, fieldErrors) {
  for (const key of Object.keys(fieldErrors)) {
    showFieldError(root, key.charAt(0).toLowerCase() + key.slice(1));
  }
}

// Wraps an async task so it cannot run twice at once. While it runs, the given
// controls are disabled. `controls` may be an array or a function returning one.
export function guard(controls, task) {
  let running = false;
  return async (...args) => {
    if (running) return undefined;
    running = true;
    const list = () => (typeof controls === "function" ? controls() : controls);
    list().forEach((control) => (control.disabled = true));
    try {
      return await task(...args);
    } finally {
      running = false;
      list().forEach((control) => (control.disabled = false));
    }
  };
}
