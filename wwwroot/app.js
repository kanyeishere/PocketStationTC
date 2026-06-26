const state = {
  token: new URLSearchParams(location.search).get("token") || localStorage.getItem("pocket.station.token") || "",
  ws: null,
  reconnectTimer: null,
  chats: [],
  filterModes: [],
  currentModeId: "all",
  allChatTypes: [],
  selectedTypes: new Set()
};

if (state.token) {
  localStorage.setItem("pocket.station.token", state.token);
}

const $ = (id) => document.getElementById(id);

function apiUrl(path) {
  const url = new URL(path, location.origin);
  if (state.token) url.searchParams.set("token", state.token);
  return url.toString();
}

function imageApiUrl(path) {
  const url = new URL(apiUrl(path));
  url.searchParams.set("t", String(Date.now()));
  return url.toString();
}

async function api(path, options = {}) {
  const headers = new Headers(options.headers || {});
  if (state.token) headers.set("X-Pocket-Token", state.token);
  return fetch(apiUrl(path), { ...options, headers });
}

function setConnection(text, mode) {
  const el = $("connection");
  el.textContent = text;
  el.className = `connection ${mode || ""}`;
}

function connectWs() {
  if (state.ws && state.ws.readyState < 2) return;

  const protocol = location.protocol === "https:" ? "wss:" : "ws:";
  const url = new URL(`${protocol}//${location.host}/ws`);
  if (state.token) url.searchParams.set("token", state.token);

  const ws = new WebSocket(url);
  state.ws = ws;
  setConnection("连接中", "");

  ws.onopen = () => setConnection("已连接", "online");
  ws.onmessage = (event) => handleEnvelope(JSON.parse(event.data));
  ws.onclose = () => {
    setConnection("已断开，重连中", "offline");
    clearTimeout(state.reconnectTimer);
    state.reconnectTimer = setTimeout(connectWs, 1500);
  };
  ws.onerror = () => setConnection("连接错误", "offline");
}

function sendEnvelope(type, payload = {}) {
  const envelope = { v: 1, id: crypto.randomUUID(), type, payload };
  if (state.ws && state.ws.readyState === WebSocket.OPEN) {
    state.ws.send(JSON.stringify(envelope));
    return Promise.resolve();
  }

  return api("/api/command", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(envelope)
  });
}

function handleEnvelope(envelope) {
  switch (envelope.type) {
    case "event.chat":
      addChat(envelope.payload);
      break;
    case "event.chat.history":
      state.chats = [];
      for (const item of envelope.payload || []) {
        if (item) state.chats.push(item);
      }
      renderChatList();
      break;
    case "event.player.snapshot":
      renderSnapshot(envelope.payload);
      break;
    case "event.screen.ready":
      renderScreenshot(envelope.payload);
      break;
    case "event.command.result":
      handleCommandResult(envelope.payload);
      break;
  }
}

function handleCommandResult(payload) {
  if (!payload) return;
  if (payload.ok && payload.data && payload.data.url) {
    renderScreenshot(payload.data);
    return;
  }

  if (!payload.ok) {
    const message = payload.message || "命令失败";
    setConnection(message, "offline");
    $("screen-meta").textContent = message;
    return;
  }

  if (payload.message === "sent") {
    setConnection("已发送", "online");
  }
}

function addChat(item, scroll = true) {
  if (!item) return;
  state.chats.push(item);
  if (state.chats.length > 500) state.chats.shift();

  if (matchesMode(item)) {
    appendChatRow(item);
    while ($("chat-list").children.length > 500) {
      $("chat-list").firstElementChild.remove();
    }

    if (scroll) scrollChat();
  }

  updateModeMeta();
}

function renderChatList() {
  const list = $("chat-list");
  list.textContent = "";
  for (const item of state.chats) {
    if (matchesMode(item)) appendChatRow(item);
  }
  updateModeMeta();
  scrollChat();
}

function appendChatRow(item) {
  const row = document.createElement("div");
  row.className = "chat-row";
  row.innerHTML = `
    <div class="chat-meta">
      <span>${escapeHtml(item.channel || "")}</span>
      <span>${escapeHtml(item.sender || "")}</span>
    </div>
    <div class="chat-message">${escapeHtml(item.message || "")}</div>
  `;
  $("chat-list").appendChild(row);
}

function scrollChat() {
  const list = $("chat-list");
  list.scrollTop = list.scrollHeight;
}

function currentMode() {
  return state.filterModes.find((mode) => mode.id === state.currentModeId) || state.filterModes[0] || {
    id: "all",
    name: "全部消息",
    isBuiltIn: true,
    enabledTypes: [],
    includeKeywords: [],
    excludeKeywords: []
  };
}

function matchesMode(item, mode = currentMode()) {
  const channel = String(item.channel || "");
  const enabledTypes = mode.enabledTypes || [];
  if (enabledTypes.length > 0 && !enabledTypes.some((type) => equalsText(type, channel))) {
    return false;
  }

  const haystack = `${item.channel || ""} ${item.sender || ""} ${item.message || ""}`.toLocaleLowerCase();
  const includeKeywords = normalizeKeywords(mode.includeKeywords || []);
  if (includeKeywords.length > 0 && !includeKeywords.some((keyword) => haystack.includes(keyword.toLocaleLowerCase()))) {
    return false;
  }

  const excludeKeywords = normalizeKeywords(mode.excludeKeywords || []);
  return !excludeKeywords.some((keyword) => haystack.includes(keyword.toLocaleLowerCase()));
}

function renderFilterSettings(settings) {
  state.filterModes = Array.isArray(settings.modes) ? settings.modes : [];
  state.allChatTypes = Array.isArray(settings.allTypes) ? settings.allTypes : [];
  state.currentModeId = settings.currentModeId || state.currentModeId || "all";

  if (!state.filterModes.some((mode) => mode.id === state.currentModeId)) {
    state.currentModeId = state.filterModes[0]?.id || "all";
  }

  localStorage.setItem("pocket.station.chatMode", state.currentModeId);
  renderModeSelect();
  renderFilterEditor();
  renderChatList();
}

function renderModeSelect() {
  const select = $("chat-mode");
  select.textContent = "";
  for (const mode of state.filterModes) {
    const option = document.createElement("option");
    option.value = mode.id;
    option.textContent = mode.name || mode.id;
    select.appendChild(option);
  }
  select.value = state.currentModeId;
}

function renderFilterEditor() {
  const mode = currentMode();
  $("mode-name").value = mode.name || "";
  $("mode-include").value = (mode.includeKeywords || []).join("\n");
  $("mode-exclude").value = (mode.excludeKeywords || []).join("\n");
  $("delete-mode").disabled = Boolean(mode.isBuiltIn);
  $("save-mode").textContent = mode.isBuiltIn ? "另存" : "保存";
  state.selectedTypes = new Set(mode.enabledTypes || []);
  renderTypePalette();
  updateModeMeta();
}

function renderTypePalette() {
  const palette = $("type-palette");
  palette.textContent = "";

  for (const type of state.allChatTypes) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = `type-chip ${hasSelectedType(type) ? "active" : ""}`;
    button.textContent = type;
    button.addEventListener("click", () => {
      toggleSelectedType(type);
      button.classList.toggle("active", hasSelectedType(type));
    });
    palette.appendChild(button);
  }
}

function hasSelectedType(type) {
  for (const selected of state.selectedTypes) {
    if (equalsText(selected, type)) return true;
  }
  return false;
}

function toggleSelectedType(type) {
  for (const selected of state.selectedTypes) {
    if (equalsText(selected, type)) {
      state.selectedTypes.delete(selected);
      return;
    }
  }
  state.selectedTypes.add(type);
}

function updateModeMeta() {
  const total = state.chats.length;
  const shown = state.chats.filter((item) => matchesMode(item)).length;
  $("mode-meta").textContent = `${shown}/${total}`;
}

function readEditorMode(forceNew = false) {
  const mode = currentMode();
  const createNew = forceNew || mode.isBuiltIn;
  return {
    id: createNew ? `custom-${Date.now()}` : mode.id,
    name: $("mode-name").value.trim() || "未命名模式",
    isBuiltIn: false,
    enabledTypes: Array.from(state.selectedTypes),
    includeKeywords: parseKeywordText($("mode-include").value),
    excludeKeywords: parseKeywordText($("mode-exclude").value)
  };
}

async function saveCurrentMode(forceNew = false) {
  const edited = readEditorMode(forceNew);
  const nextModes = [...state.filterModes];
  const existingIndex = nextModes.findIndex((mode) => mode.id === edited.id);
  if (existingIndex >= 0) {
    nextModes[existingIndex] = edited;
  } else {
    nextModes.push(edited);
  }

  await saveFilterSettings(nextModes, edited.id);
}

async function deleteCurrentMode() {
  const mode = currentMode();
  if (mode.isBuiltIn) return;

  const nextModes = state.filterModes.filter((item) => item.id !== mode.id);
  await saveFilterSettings(nextModes, "all");
}

async function selectCurrentMode(modeId) {
  state.currentModeId = modeId;
  localStorage.setItem("pocket.station.chatMode", state.currentModeId);
  renderFilterEditor();
  renderChatList();
  await saveFilterSettings(state.filterModes, modeId, false);
}

async function saveFilterSettings(modes, currentModeId, showSaved = true) {
  try {
    const response = await api("/api/chat/modes", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        currentModeId,
        modes,
        allTypes: state.allChatTypes
      })
    });
    const result = await response.json();
    if (!response.ok) {
      throw new Error(result.message || result.error || "保存失败");
    }

    renderFilterSettings(result);
    if (showSaved) setConnection("筛选已保存", "online");
  } catch (error) {
    setConnection(String(error), "offline");
  }
}

function parseKeywordText(value) {
  return normalizeKeywords(String(value).split(/[\n,，]+/));
}

function normalizeKeywords(values) {
  return values
    .map((value) => String(value).trim())
    .filter((value, index, array) => value && array.findIndex((other) => equalsText(other, value)) === index);
}

function equalsText(left, right) {
  return String(left).toLocaleLowerCase() === String(right).toLocaleLowerCase();
}

function renderSnapshot(snapshot) {
  if (!snapshot) return;
  renderCharacter($("player-card"), snapshot.localPlayer, "未登录");
  renderCharacter($("target-card"), snapshot.target, "无目标");

  const party = $("party-list");
  party.textContent = "";
  for (const member of snapshot.party || []) {
    const el = document.createElement("div");
    el.className = "member";
    renderCharacter(el, member, "未知成员");
    party.appendChild(el);
  }
}

function renderCharacter(container, character, emptyText) {
  if (!character) {
    container.textContent = emptyText;
    return;
  }

  const hpPct = pct(character.currentHp, character.maxHp);
  const mpPct = pct(character.currentMp, character.maxMp);
  const pos = character.position || {};

  container.innerHTML = `
    <div class="stat-name">${escapeHtml(character.name || "Unknown")}</div>
    <div class="bars">
      <div class="bar"><span style="width:${hpPct}%"></span></div>
      <div class="bar mp"><span style="width:${mpPct}%"></span></div>
    </div>
    <div class="meta-grid">
      <div>HP ${character.currentHp || 0}/${character.maxHp || 0}</div>
      <div>MP ${character.currentMp || 0}/${character.maxMp || 0}</div>
      <div>Job ${character.classJobId || 0} Lv.${character.level || 0}</div>
      <div>${Number(pos.x || pos.X || 0).toFixed(1)}, ${Number(pos.y || pos.Y || 0).toFixed(1)}, ${Number(pos.z || pos.Z || 0).toFixed(1)}</div>
    </div>
  `;
}

function renderScreenshot(payload) {
  if (!payload) return;
  const img = $("screen-image");
  img.src = imageApiUrl(payload.url || "/api/screen/latest.jpg");
  $("screen-meta").textContent = `${payload.width} x ${payload.height}`;
}

async function requestScreenshot() {
  $("screen-meta").textContent = "请求中";
  $("capture").disabled = true;

  try {
    const response = await api("/api/screen/capture", { method: "POST" });
    const result = await response.json();
    if (!response.ok && result && result.message) {
      throw new Error(result.message);
    }
    handleCommandResult(result);
  } catch (error) {
    const message = String(error);
    setConnection(message, "offline");
    $("screen-meta").textContent = message;
  } finally {
    $("capture").disabled = false;
  }
}

async function requestSendChat(content) {
  const button = $("send-chat");
  const channel = $("chat-channel").value || "";
  const outgoing = content.startsWith("/") || !channel ? content : `${channel} ${content}`;
  button.disabled = true;
  setConnection("发送中", "");

  try {
    const response = await api("/api/chat/send", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ content: outgoing })
    });
    const result = await response.json();
    handleCommandResult(result);
    if (!response.ok || !result.ok) {
      throw new Error(result.message || "发送失败");
    }
    return true;
  } catch (error) {
    const message = String(error);
    setConnection(message, "offline");
    return false;
  } finally {
    button.disabled = false;
  }
}

function pct(value, max) {
  if (!max) return 0;
  return Math.max(0, Math.min(100, Math.round((value / max) * 100)));
}

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}

async function loadInitial() {
  try {
    const [health, modes, history, snapshot] = await Promise.all([
      api("/api/health").then((r) => r.json()),
      api("/api/chat/modes").then((r) => r.json()),
      api("/api/chat/history").then((r) => r.json()),
      api("/api/state").then((r) => r.json())
    ]);

    $("server-info").textContent = JSON.stringify(health, null, 2);
    renderFilterSettings(modes);
    handleEnvelope({ type: "event.chat.history", payload: history });
    handleEnvelope({ type: "event.player.snapshot", payload: snapshot });
  } catch (error) {
    setConnection(String(error), "offline");
  }
}

document.querySelectorAll(".bottom-tabs button").forEach((button) => {
  button.addEventListener("click", () => {
    document.querySelectorAll(".bottom-tabs button").forEach((x) => x.classList.remove("active"));
    document.querySelectorAll(".view").forEach((x) => x.classList.remove("active"));
    button.classList.add("active");
    $(`tab-${button.dataset.tab}`).classList.add("active");
  });
});

$("chat-form").addEventListener("submit", async (event) => {
  event.preventDefault();
  const input = $("chat-input");
  const content = input.value.trim();
  if (!content) return;
  if (await requestSendChat(content)) {
    input.value = "";
  }
});

$("toggle-filter-editor").addEventListener("click", () => {
  $("filter-editor").classList.toggle("hidden");
});

$("chat-mode").addEventListener("change", (event) => {
  selectCurrentMode(event.target.value);
});

$("save-mode").addEventListener("click", () => saveCurrentMode(false));
$("new-mode").addEventListener("click", () => saveCurrentMode(true));
$("delete-mode").addEventListener("click", deleteCurrentMode);
$("capture").addEventListener("click", requestScreenshot);
$("refresh").addEventListener("click", loadInitial);

document.querySelectorAll("[data-command]").forEach((button) => {
  button.addEventListener("click", () => {
    const command = button.dataset.command || "";
    $("chat-input").value = command;
    $("chat-input").focus();
  });
});

loadInitial();
connectWs();
