// Entry point: wires the view switch and initializes both views.
import { initEmployeeView, refreshEmployeeView } from "./employee.js";
import { initManagerView, refreshManagerView } from "./manager.js";

const views = {
  employee: { section: document.getElementById("employee-view"), refresh: refreshEmployeeView },
  manager: { section: document.getElementById("manager-view"), refresh: refreshManagerView },
};
const switchButtons = document.querySelectorAll("button[data-view]");

// Only the view choice is stored: there is no identity or login.
function showView(name) {
  for (const [key, view] of Object.entries(views)) {
    view.section.hidden = key !== name;
  }
  switchButtons.forEach((button) => {
    button.setAttribute("aria-pressed", String(button.dataset.view === name));
  });
  views[name].refresh();
}

switchButtons.forEach((button) => {
  button.addEventListener("click", () => showView(button.dataset.view));
});

initEmployeeView();
initManagerView();
showView("employee");
