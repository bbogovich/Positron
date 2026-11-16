// Text editor logic. Uses only the built-in bridges Positron injects as window.positron:
//   positron.dialog.open / positron.dialog.save  -> native file dialogs, resolve to a path or null
//   positron.fs.readText / positron.fs.writeText -> text file I/O by absolute path

const editor = document.getElementById("editor");
const pathLabel = document.getElementById("path");

// The absolute path of the file currently being edited, or null for an unsaved buffer.
let currentPath = null;

function setPath(path) {
  currentPath = path;
  pathLabel.textContent = path || "(no file)";
}

async function open() {
  const path = await window.positron.dialog.open({ allowedExtensions: ["txt", "md"] });
  if (!path) return; // user cancelled
  editor.value = await window.positron.fs.readText(path);
  setPath(path);
}

async function save() {
  // No file yet? Ask where to put it first.
  let path = currentPath;
  if (!path) {
    path = await window.positron.dialog.save({
      allowedExtensions: ["txt", "md"],
      suggestedFileName: "untitled.txt",
    });
    if (!path) return; // user cancelled
  }
  await window.positron.fs.writeText(path, editor.value);
  setPath(path);
}

document.getElementById("open").addEventListener("click", open);
document.getElementById("save").addEventListener("click", save);

// --- Minimal custom context menu --------------------------------------------------------------
const menu = document.getElementById("menu");

document.addEventListener("contextmenu", (e) => {
  e.preventDefault();
  menu.style.left = e.clientX + "px";
  menu.style.top = e.clientY + "px";
  menu.style.display = "block";
});

// Dismiss the menu on any left click or Escape.
document.addEventListener("click", () => { menu.style.display = "none"; });
document.addEventListener("keydown", (e) => { if (e.key === "Escape") menu.style.display = "none"; });

document.getElementById("menu-save").addEventListener("click", () => {
  menu.style.display = "none";
  save();
});
