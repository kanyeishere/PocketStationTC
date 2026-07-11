<script setup lang="ts">
import { onMounted, onUnmounted, ref, watch } from "vue";
import BottomTabs from "@/components/BottomTabs.vue";
import TopBar from "@/components/TopBar.vue";
import { useThemePreset } from "@/composables/useThemePreset";
import { usePocketStation } from "@/composables/usePocketStation";
import ChatView from "@/views/ChatView.vue";
import LiveView from "@/views/LiveView.vue";
import ShortcutsView from "@/views/ShortcutsView.vue";
import StateView from "@/views/StateView.vue";
import type { TabKey } from "@/types";

const tabRoutes: Record<TabKey, string> = {
  chat: "/",
  state: "/state",
  shortcuts: "/shortcuts",
  live: "/live"
};

const routeTabs: Record<string, TabKey> = {
  "/": "chat",
  "/chat": "chat",
  "/state": "state",
  "/shortcuts": "shortcuts",
  "/live": "live"
};

function normalizePath(path: string) {
  return path.replace(/\/+$/, "") || "/";
}

function tabFromLocation() {
  return routeTabs[normalizePath(window.location.pathname)] || "chat";
}

function currentRoute() {
  return `${window.location.pathname}${window.location.search}`;
}

function routeForTab(tab: TabKey) {
  return `${tabRoutes[tab]}${window.location.search}`;
}

const activeTab = ref<TabKey>(tabFromLocation());
const chatDraft = ref("");
const { activePreset, activeTheme, themePresets } = useThemePreset();
const {
  allChatTypes,
  chats,
  connectionMode,
  connectionText,
  currentMode,
  currentModeId,
  filteredChats,
  filterModes,
  sendLoading,
  snapshot,
  connectWs,
  deleteCurrentMode,
  liveFps,
  liveFrame,
  liveFrameSize,
  liveRunning,
  liveWaiting,
  loadInitial,
  loadPlugins,
  loadShortcuts,
  saveMode,
  saveShortcuts,
  selectMode,
  sendChat,
  sendShortcut,
  shortcuts,
  reconnectWs,
  recoverLiveStream,
  togglePlugin,
  plugins,
  pluginsLoaded,
  dailyRoutinesModules,
  dailyRoutinesLoading,
  loadDailyRoutines,
  toggleDailyRoutine,
  startStream,
  stopStream,
  syncStreamConfig
} = usePocketStation();

let wasHidden = document.visibilityState === "hidden";

onMounted(() => {
  replaceRoute(activeTab.value);
  window.addEventListener("popstate", syncTabFromRoute);
  window.addEventListener("pageshow", onPageShow);
  window.addEventListener("online", onOnline);
  window.addEventListener("focus", onFocus);
  document.addEventListener("visibilitychange", onVisibilityChange);
  loadInitial();
  loadShortcuts();
  loadDailyRoutines();
  if (activeTab.value === "state") {
    loadPlugins();
  }
  connectWs();
});

onUnmounted(() => {
  window.removeEventListener("popstate", syncTabFromRoute);
  window.removeEventListener("pageshow", onPageShow);
  window.removeEventListener("online", onOnline);
  window.removeEventListener("focus", onFocus);
  document.removeEventListener("visibilitychange", onVisibilityChange);
});

watch(activeTab, (tab) => {
  pushRoute(tab);
  if (tab === "state") {
    loadPlugins();
    loadDailyRoutines();
  }
});

function pushRoute(tab: TabKey) {
  const route = routeForTab(tab);
  if (route !== currentRoute()) {
    window.history.pushState({ tab }, "", route);
  }
}

function replaceRoute(tab: TabKey) {
  const route = routeForTab(tab);
  if (route !== currentRoute()) {
    window.history.replaceState({ tab }, "", route);
  }
}

function syncTabFromRoute() {
  activeTab.value = tabFromLocation();
}

function doSendShortcut(command: string) {
  sendShortcut(command);
  activeTab.value = "chat";
}

function onVisibilityChange() {
  if (document.visibilityState === "hidden") {
    wasHidden = true;
    return;
  }

  if (!wasHidden) {
    return;
  }

  wasHidden = false;
  recoverAfterResume(true);
}

function onPageShow(event: PageTransitionEvent) {
  recoverAfterResume(event.persisted);
}

function onOnline() {
  recoverAfterResume(true);
}

function onFocus() {
  recoverAfterResume(false);
}

function recoverAfterResume(forceReconnect: boolean) {
  if (forceReconnect) {
    reconnectWs();
  } else {
    connectWs();
  }

  if (activeTab.value === "live") {
    void recoverLiveStream(forceReconnect);
    return;
  }

  void syncStreamConfig().catch(() => {});
}
</script>

<template>
  <main class="app">
    <TopBar
      v-model:theme="activeTheme"
      :connection-mode="connectionMode"
      :connection-text="connectionText"
      :theme-presets="themePresets"
      :active-preset="activePreset"
      @refresh="loadInitial"
    />

    <ChatView
      v-model:draft="chatDraft"
      :all-chat-types="allChatTypes"
      :chats="filteredChats"
      :class="{ active: activeTab === 'chat' }"
      :current-mode="currentMode"
      :current-mode-id="currentModeId"
      :delete-current-mode="deleteCurrentMode"
      :filter-modes="filterModes"
      :save-mode="saveMode"
      :select-mode="selectMode"
      :send-chat="sendChat"
      :send-loading="sendLoading"
      :total-chat-count="chats.length"
    />

    <StateView
      :class="{ active: activeTab === 'state' }"
      :snapshot="snapshot"
      :plugins="plugins"
      :plugins-loaded="pluginsLoaded"
      :toggle-plugin="togglePlugin"
      :daily-routines-modules="dailyRoutinesModules"
      :daily-routines-loading="dailyRoutinesLoading"
      :toggle-daily-routine="toggleDailyRoutine"
      @refresh-plugins="loadPlugins"
    />

    <ShortcutsView
      :class="{ active: activeTab === 'shortcuts' }"
      :shortcuts="shortcuts"
      @save="saveShortcuts"
      @send="doSendShortcut"
    />

    <LiveView
      :class="{ active: activeTab === 'live' }"
      :is-active="activeTab === 'live'"
      :fps="liveFps"
      :running="liveRunning"
      :frame="liveFrame"
      :frame-size="liveFrameSize"
      :waiting="liveWaiting"
      @start="startStream"
      @stop="stopStream"
    />
  </main>

  <BottomTabs v-model:active-tab="activeTab" />
</template>
