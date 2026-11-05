// Плагин Vallo: собирает тексты выделенных слоёв, отдаёт их свитчеру и применяет его правки.
// Правил типографики здесь нет — всё решает свитчер. Протокол — PROTOCOL.md.

// Плагин живёт одно нажатие (решение автора: в фоне он висел бы с плашкой «Running Vallo»):
// запустили → свитчер прислал press → оттипографировали → окошко итога → «Готово» закрывает плагин.
figma.showUI(__html__, { visible: false, width: 408, height: 80, title: "Vallo Typograph" });
figma.ui.postMessage({ type: "file", name: figma.root.name });

// свитчер не ответил на запуск (не запущен) — не висеть с плашкой
let waitTimer = setTimeout(() => figma.closePlugin("Нужно запущенное приложение Vallo Switcher (бесплатно): github.com/madvito-collab/vallo_switcher"), 6000);

// связь есть, но работы нет — свитчер занят другим файлом; ждём дольше и пишем правду
function onConnected() {
  if (!waitTimer) return;
  clearTimeout(waitTimer);
  waitTimer = setTimeout(() => figma.closePlugin("Vallo Switcher занят — попробуйте ещё раз"), 60000);
}

// свитчер пропал (перезапуск, выход) посреди работы — сразу закрыться, а не ждать срока
function onDisconnected() {
  if (pending.size > 0) figma.closePlugin("Связь с Vallo Switcher прервалась — попробуйте ещё раз");
}

function toSwitcher(message) {
  figma.ui.postMessage({ type: "send", message });
}

// Нажатие в работе: id → слои на момент нажатия (выделение потом могли сменить — пишем в те же слои).
const pending = new Map();
let progress = null;

function collectTexts() {
  const seen = new Set();
  const nodes = [];
  for (const node of figma.currentPage.selection) {
    const found = node.type === "TEXT" ? [node]
      : ("findAllWithCriteria" in node ? node.findAllWithCriteria({ types: ["TEXT"] }) : []);
    for (const t of found) {
      if (!seen.has(t.id)) { seen.add(t.id); nodes.push(t); }
    }
  }
  return nodes;
}

function layersWord(n) {
  const m10 = n % 10, m100 = n % 100;
  if (m10 === 1 && m100 !== 11) return "слой";
  if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return "слоя";
  return "слоёв";
}

function onPress(id) {
  clearTimeout(waitTimer);
  waitTimer = null;
  if (figma.currentPage.selectedTextRange) {
    toSwitcher({ type: "mode", id, mode: "text" });
    return setTimeout(() => figma.closePlugin(), 300);
  }
  if (figma.currentPage.selection.length === 0) {
    toSwitcher({ type: "mode", id, mode: "empty" });
    return setTimeout(() => figma.closePlugin("Выделите фреймы или слои"), 300);
  }
  const nodes = collectTexts();
  const usable = nodes.filter((n) => !n.hasMissingFont);
  // запоминаем и исходный текст: если слой поменяли, пока свитчер думал, правки по старым смещениям его испортят
  pending.set(id, new Map(usable.map((n) => [n.id, { node: n, text: n.characters }])));
  if (progress) progress.cancel();
  progress = figma.notify(`Vallo: типографирую ${usable.length} ${layersWord(usable.length)}…`, { timeout: Infinity });
  figma.ui.hide();
  // свитчер пропал посреди работы — не оставлять плашку навсегда
  setTimeout(() => {
    if (!pending.has(id)) return;
    figma.closePlugin("Vallo Switcher не ответил — попробуйте ещё раз");
  }, 30000 + usable.length * 4000);   // модель — до 3 с на слой (холодный старт дольше)
  toSwitcher({ type: "mode", id, mode: "layers", skipped: nodes.length - usable.length,
               texts: usable.map((n) => ({ id: n.id, text: n.characters })) });
}

async function applyEdits(node, original, edits) {
  if (node.removed || node.hasMissingFont || node.characters !== original) return false;
  const fonts = node.getStyledTextSegments(["fontName"]).map((s) => s.fontName);
  await Promise.all(fonts.map((f) => figma.loadFontAsync(f)));
  // с конца — смещения правок считаны по исходному тексту
  for (let i = edits.length - 1; i >= 0; i--) {
    const { start, end, insert } = edits[i];
    if (insert.length > 0) node.insertCharacters(end, insert, end > 0 ? "BEFORE" : "AFTER");
    if (end > start) node.deleteCharacters(start, end);
  }
  return true;
}

async function onResult(message) {
  const nodes = pending.get(message.id);
  pending.delete(message.id);
  if (!nodes) return;
  let failed = 0;
  for (const item of message.edits) {
    const entry = nodes.get(item.id);
    try {
      if (!entry || !(await applyEdits(entry.node, entry.text, item.edits))) failed++;
    } catch (e) {
      failed++;
    }
  }
  figma.commitUndo();   // один запуск — один шаг ⌘Z
  if (progress) { progress.cancel(); progress = null; }
  const stats = Object.assign({}, message.stats, { layers: message.stats.layers - failed });
  figma.ui.postMessage({ type: "stats", stats, skipped: message.skipped, failed });
  toSwitcher({ type: "done", id: message.id });
}

figma.ui.onmessage = (m) => {
  if (m.type === "show") return figma.ui.show();
  if (m.type === "resize") return figma.ui.resize(408, m.height);
  if (m.type === "hide") return figma.closePlugin();
  if (m.type === "connected") return onConnected();
  if (m.type === "disconnected") return onDisconnected();
  if (m.from !== "switcher") return;
  const message = m.message;
  if (message.type === "press") onPress(message.id);
  else if (message.type === "result") onResult(message);
};
