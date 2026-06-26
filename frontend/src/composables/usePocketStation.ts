import { computed, onBeforeUnmount, ref, shallowRef } from "vue";
import {
  createEnvelope,
  getJson,
  imageApiUrl,
  postJson,
  websocketUrl
} from "@/services/pocketApi";
import type {
  ChatEvent,
  ChatFilterMode,
  ChatFilterSettings,
  CommandResult,
  ConnectionMode,
  Envelope,
  HealthInfo,
  PlayerSnapshot,
  ScreenshotReadyEvent
} from "@/types";

const maxChatRows = 500;
const defaultMode: ChatFilterMode = {
  id: "all",
  name: "全部消息",
  isBuiltIn: true,
  enabledTypes: [],
  includeKeywords: [],
  excludeKeywords: []
};

export function usePocketStation() {
  const connectionText = ref("连接中");
  const connectionMode = ref<ConnectionMode>("");
  const ws = shallowRef<WebSocket | null>(null);
  const chats = ref<ChatEvent[]>([]);
  const snapshot = ref<PlayerSnapshot | null>(null);
  const screenshot = ref<ScreenshotReadyEvent | null>(null);
  const screenMeta = ref("");
  const serverInfo = ref<HealthInfo | null>(null);
  const filterModes = ref<ChatFilterMode[]>([]);
  const currentModeId = ref("all");
  const allChatTypes = ref<string[]>([]);
  const screenshotLoading = ref(false);
  const sendLoading = ref(false);
  const liveFps = ref(30);
  const liveRunning = ref(false);
  const liveFrame = shallowRef<ImageBitmap | null>(null);
  const liveFrameSize = ref("");
  let reconnectTimer: number | undefined;

  const currentMode = computed(() => {
    return filterModes.value.find((mode) => mode.id === currentModeId.value) || filterModes.value[0] || defaultMode;
  });

  const filteredChats = computed(() => {
    const mode = currentMode.value;
    return chats.value.filter((item) => matchesMode(item, mode));
  });

  const screenImageUrl = computed(() => {
    if (!screenshot.value) {
      return "";
    }

    return imageApiUrl(screenshot.value.url || "/api/screen/latest.jpg");
  });

  function setConnection(text: string, mode: ConnectionMode = "") {
    connectionText.value = text;
    connectionMode.value = mode;
  }

  function connectWs() {
    if (ws.value && ws.value.readyState < WebSocket.CLOSING) {
      return;
    }

    const socket = new WebSocket(websocketUrl());
    socket.binaryType = "arraybuffer";
    ws.value = socket;
    setConnection("连接中");

    socket.onopen = () => setConnection("已连接", "online");
    socket.onmessage = (event) => {
      if (event.data instanceof ArrayBuffer) {
        handleBinaryFrame(event.data);
        return;
      }

      try {
        handleEnvelope(JSON.parse(event.data) as Envelope);
      } catch (error) {
        setConnection(String(error), "offline");
      }
    };
    socket.onclose = () => {
      setConnection("已断开，重连中", "offline");
      window.clearTimeout(reconnectTimer);
      reconnectTimer = window.setTimeout(connectWs, 1500);
    };
    socket.onerror = () => setConnection("连接错误", "offline");
  }

  function closeWs() {
    window.clearTimeout(reconnectTimer);
    reconnectTimer = undefined;
    ws.value?.close();
    ws.value = null;
  }

  async function sendEnvelope(type: string, payload: unknown = {}) {
    const envelope = createEnvelope(type, payload);
    if (ws.value?.readyState === WebSocket.OPEN) {
      ws.value.send(JSON.stringify(envelope));
      return;
    }

    await postJson<CommandResult>("/api/command", envelope);
  }

  function handleEnvelope(envelope: Envelope) {
    switch (envelope.type) {
      case "event.chat":
        addChat(envelope.payload as ChatEvent);
        break;
      case "event.chat.history":
        chats.value = Array.isArray(envelope.payload) ? (envelope.payload as ChatEvent[]) : [];
        break;
      case "event.player.snapshot":
        snapshot.value = envelope.payload as PlayerSnapshot;
        break;
      case "event.screen.ready":
        renderScreenshot(envelope.payload as ScreenshotReadyEvent);
        break;
      case "event.command.result":
        handleCommandResult(envelope.payload as CommandResult);
        break;
    }
  }

  function addChat(item: ChatEvent | null | undefined) {
    if (!item) {
      return;
    }

    chats.value.push(item);
    if (chats.value.length > maxChatRows) {
      chats.value.splice(0, chats.value.length - maxChatRows);
    }
  }

  function handleCommandResult(result: CommandResult | null | undefined) {
    if (!result) {
      return;
    }

    const data = result.data as Partial<ScreenshotReadyEvent> | undefined;
    if (result.ok && data && typeof data.url === "string") {
      renderScreenshot(data as ScreenshotReadyEvent);
      return;
    }

    if (!result.ok) {
      const message = result.message || "命令失败";
      setConnection(message, "offline");
      screenMeta.value = message;
      return;
    }

    if (result.message === "sent") {
      setConnection("已发送", "online");
    }
  }

  function renderScreenshot(payload: ScreenshotReadyEvent) {
    screenshot.value = payload;
    screenMeta.value = `${payload.width} x ${payload.height}`;
  }

  async function requestScreenshot() {
    screenshotLoading.value = true;
    screenMeta.value = "请求中";

    try {
      const result = await postJson<CommandResult<ScreenshotReadyEvent>>("/api/screen/capture", {});
      handleCommandResult(result);
    } catch (error) {
      const message = String(error);
      setConnection(message, "offline");
      screenMeta.value = message;
    } finally {
      screenshotLoading.value = false;
    }
  }

  async function handleBinaryFrame(data: ArrayBuffer) {
    try {
      const blob = new Blob([data], { type: "image/jpeg" });
      const bitmap = await createImageBitmap(blob);
      liveFrame.value = bitmap;
      liveFrameSize.value = `${bitmap.width} x ${bitmap.height}`;
    } catch (error) {
      // Ignore corrupt frames — just skip
    }
  }

  async function startStream(fps?: number) {
    const targetFps = fps ?? liveFps.value;
    try {
      await sendEnvelope("cmd.startStream", { fps: targetFps });
      liveFps.value = targetFps;
      liveRunning.value = true;
    } catch (error) {
      liveRunning.value = false;
      throw error;
    }
  }

  async function stopStream() {
    try {
      await sendEnvelope("cmd.stopStream", {});
    } finally {
      liveRunning.value = false;
      liveFrame.value = null;
    }
  }

  async function sendChat(content: string, channel: string): Promise<boolean> {
    const outgoing = content.startsWith("/") || !channel ? content : `${channel} ${content}`;
    sendLoading.value = true;
    setConnection("发送中");

    try {
      const result = await postJson<CommandResult>("/api/chat/send", { content: outgoing });
      handleCommandResult(result);
      if (!result.ok) {
        throw new Error(result.message || "发送失败");
      }

      return true;
    } catch (error) {
      setConnection(String(error), "offline");
      return false;
    } finally {
      sendLoading.value = false;
    }
  }

  async function loadInitial() {
    try {
      const [health, modes, history, state] = await Promise.all([
        getJson<HealthInfo>("/api/health"),
        getJson<ChatFilterSettings>("/api/chat/modes"),
        getJson<ChatEvent[]>("/api/chat/history"),
        getJson<PlayerSnapshot>("/api/state")
      ]);

      serverInfo.value = health;
      setFilterSettings(modes);
      chats.value = Array.isArray(history) ? history : [];
      snapshot.value = state;
    } catch (error) {
      setConnection(String(error), "offline");
    }
  }

  async function selectMode(modeId: string) {
    currentModeId.value = modeId;
    await saveFilterSettings(filterModes.value, modeId, false);
  }

  async function saveMode(mode: ChatFilterMode) {
    const modes = [...filterModes.value];
    const index = modes.findIndex((item) => item.id === mode.id);
    if (index >= 0) {
      modes[index] = mode;
    } else {
      modes.push(mode);
    }

    await saveFilterSettings(modes, mode.id);
  }

  async function deleteCurrentMode() {
    const mode = currentMode.value;
    if (mode.isBuiltIn) {
      return;
    }

    const modes = filterModes.value.filter((item) => item.id !== mode.id);
    await saveFilterSettings(modes, "all");
  }

  async function saveFilterSettings(modes: ChatFilterMode[], modeId: string, showSaved = true) {
    try {
      const settings = await postJson<ChatFilterSettings>("/api/chat/modes", {
        currentModeId: modeId,
        modes,
        allTypes: allChatTypes.value
      });

      setFilterSettings(settings);
      if (showSaved) {
        setConnection("筛选已保存", "online");
      }
    } catch (error) {
      setConnection(String(error), "offline");
    }
  }

  function setFilterSettings(settings: ChatFilterSettings) {
    filterModes.value = Array.isArray(settings.modes) ? settings.modes : [];
    allChatTypes.value = Array.isArray(settings.allTypes) ? settings.allTypes : [];
    currentModeId.value = settings.currentModeId || "all";

    if (!filterModes.value.some((mode) => mode.id === currentModeId.value)) {
      currentModeId.value = filterModes.value[0]?.id || "all";
    }
  }

  onBeforeUnmount(closeWs);

  return {
    allChatTypes,
    chats,
    connectionMode,
    connectionText,
    currentMode,
    currentModeId,
    filteredChats,
    filterModes,
    screenImageUrl,
    screenMeta,
    screenshotLoading,
    sendLoading,
    serverInfo,
    snapshot,
    closeWs,
    connectWs,
    deleteCurrentMode,
    loadInitial,
    liveFps,
    liveFrame,
    liveFrameSize,
    liveRunning,
    requestScreenshot,
    saveMode,
    selectMode,
    sendChat,
    sendEnvelope,
    startStream,
    stopStream
  };
}

function matchesMode(item: ChatEvent, mode: ChatFilterMode) {
  const enabledTypes = mode.enabledTypes || [];
  if (enabledTypes.length > 0 && !enabledTypes.some((type) => equalsText(type, item.channel))) {
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

function normalizeKeywords(values: string[]) {
  return values
    .map((value) => value.trim())
    .filter((value, index, array) => value && array.findIndex((other) => equalsText(other, value)) === index);
}

function equalsText(left: string, right: string) {
  return left.toLocaleLowerCase() === right.toLocaleLowerCase();
}
