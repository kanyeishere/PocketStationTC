import { ref } from "vue";
import { postJson } from "@/services/pocketApi";
import { useChatState } from "@/composables/useChatState";
import { useConnectionStatus } from "@/composables/useConnectionStatus";
import { useDailyRoutines } from "@/composables/useDailyRoutines";
import { useLiveStream } from "@/composables/useLiveStream";
import { usePlayerState } from "@/composables/usePlayerState";
import { usePlugins } from "@/composables/usePlugins";
import { usePocketConnection } from "@/composables/usePocketConnection";
import { useScreenCapture } from "@/composables/useScreenCapture";
import { useShortcuts } from "@/composables/useShortcuts";
import { getJson } from "@/services/pocketApi";
import type {
  ChatEvent,
  ChatFilterSettings,
  CommandResult,
  Envelope,
  HealthInfo,
  PlayerSnapshot,
  ScreenshotReadyEvent,
  StreamConfig
} from "@/types";

export function usePocketStation() {
  const serverInfo = ref<HealthInfo | null>(null);
  const sendLoading = ref(false);

  const connectionStatus = useConnectionStatus();
  const chat = useChatState(connectionStatus.setConnection);
  const playerState = usePlayerState();
  const screen = useScreenCapture(connectionStatus.setConnection);
  const shortcuts = useShortcuts();
  const plugins = usePlugins();

  let connection: ReturnType<typeof usePocketConnection>;
  const liveStream = useLiveStream((type, payload) => connection.sendEnvelope(type, payload));
  const dailyRoutines = useDailyRoutines(sendChat);

  function handleEnvelope(envelope: Envelope) {
    switch (envelope.type) {
      case "event.chat":
        chat.addChat(envelope.payload as ChatEvent);
        break;
      case "event.chat.history":
        chat.setHistory(envelope.payload);
        break;
      case "event.player.snapshot":
        playerState.setSnapshot(envelope.payload as PlayerSnapshot);
        break;
      case "event.screen.ready":
        screen.renderScreenshot(envelope.payload as ScreenshotReadyEvent);
        break;
      case "event.command.result":
        handleCommandResult(envelope.payload as CommandResult);
        break;
    }
  }

  connection = usePocketConnection(connectionStatus.setConnection, {
    onBinaryFrame: liveStream.handleBinaryFrame,
    onEnvelope: handleEnvelope,
    onOpen: () => liveStream.recoverStreamIfStale()
  });

  function handleCommandResult(result: CommandResult | null | undefined) {
    if (!result) {
      return;
    }

    const data = result.data as Partial<ScreenshotReadyEvent> | undefined;
    if (result.ok && data && typeof data.url === "string") {
      screen.renderScreenshot(data as ScreenshotReadyEvent);
      return;
    }

    if (!result.ok) {
      const message = result.message || "命令失败";
      connectionStatus.setConnection(message, "offline");
      screen.setScreenError(message);
      return;
    }

    connectionStatus.setConnection(result.message || "已发送", "online");
  }

  async function loadInitial() {
    try {
      const [health, modes, history, state, streamConfig] = await Promise.all([
        getJson<HealthInfo>("/api/health"),
        getJson<ChatFilterSettings>("/api/chat/modes"),
        getJson<ChatEvent[]>("/api/chat/history"),
        getJson<PlayerSnapshot>("/api/state"),
        getJson<StreamConfig>("/api/stream/config")
      ]);

      serverInfo.value = health;
      chat.setFilterSettings(modes);
      chat.setHistory(history);
      playerState.setSnapshot(state);
      liveStream.applyStreamConfig(streamConfig);
    } catch (error) {
      connectionStatus.setConnection(String(error), "offline");
    }
  }

  async function sendChat(content: string, channel: string): Promise<boolean> {
    const outgoing = content.startsWith("/") || !channel ? content : `${channel} ${content}`;
    sendLoading.value = true;
    connectionStatus.setConnection("发送中");

    try {
      const result = await postJson<CommandResult>("/api/chat/send", { content: outgoing });
      handleCommandResult(result);
      if (!result.ok) {
        throw new Error(result.message || "发送失败");
      }

      return true;
    } catch (error) {
      connectionStatus.setConnection(String(error), "offline");
      return false;
    } finally {
      sendLoading.value = false;
    }
  }

  async function sendShortcut(command: string) {
    await sendChat(command, "");
  }

  return {
    allChatTypes: chat.allChatTypes,
    chats: chat.chats,
    closeWs: connection.closeWs,
    connectWs: connection.connectWs,
    connectionMode: connectionStatus.connectionMode,
    connectionText: connectionStatus.connectionText,
    currentMode: chat.currentMode,
    currentModeId: chat.currentModeId,
    dailyRoutinesLoading: dailyRoutines.dailyRoutinesLoading,
    dailyRoutinesModules: dailyRoutines.dailyRoutinesModules,
    deleteCurrentMode: chat.deleteCurrentMode,
    filteredChats: chat.filteredChats,
    filterModes: chat.filterModes,
    liveFps: liveStream.liveFps,
    liveFrame: liveStream.liveFrame,
    liveFrameSize: liveStream.liveFrameSize,
    liveRunning: liveStream.liveRunning,
    liveWaiting: liveStream.liveWaiting,
    loadDailyRoutines: dailyRoutines.loadDailyRoutines,
    loadInitial,
    loadPlugins: plugins.loadPlugins,
    loadShortcuts: shortcuts.loadShortcuts,
    plugins: plugins.plugins,
    pluginsLoaded: plugins.pluginsLoaded,
    reconnectWs: connection.reconnectWs,
    recoverLiveStream: liveStream.recoverStreamIfStale,
    requestScreenshot: screen.requestScreenshot,
    saveMode: chat.saveMode,
    saveShortcuts: shortcuts.saveShortcuts,
    screenImageUrl: screen.screenImageUrl,
    screenMeta: screen.screenMeta,
    screenshotLoading: screen.screenshotLoading,
    selectMode: chat.selectMode,
    sendChat,
    sendEnvelope: connection.sendEnvelope,
    sendLoading,
    sendShortcut,
    serverInfo,
    shortcuts: shortcuts.shortcuts,
    snapshot: playerState.snapshot,
    startStream: liveStream.startStream,
    stopStream: liveStream.stopStream,
    syncStreamConfig: liveStream.syncStreamConfig,
    toggleDailyRoutine: dailyRoutines.toggleDailyRoutine,
    togglePlugin: plugins.togglePlugin
  };
}
