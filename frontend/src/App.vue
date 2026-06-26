<script setup lang="ts">
import { onMounted, ref, watch } from "vue";
import BottomTabs from "@/components/BottomTabs.vue";
import TopBar from "@/components/TopBar.vue";
import { usePocketStation } from "@/composables/usePocketStation";
import ChatView from "@/views/ChatView.vue";
import CommandsView from "@/views/CommandsView.vue";
import LiveView from "@/views/LiveView.vue";
import ShortcutsView from "@/views/ShortcutsView.vue";
import StateView from "@/views/StateView.vue";
import type { TabKey } from "@/types";

const activeTab = ref<TabKey>("chat");
const chatDraft = ref("");
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
  serverInfo,
  snapshot,
  connectWs,
  deleteCurrentMode,
  liveFps,
  liveFrame,
  liveFrameSize,
  liveRunning,
  loadInitial,
  loadPlugins,
  loadShortcuts,
  saveMode,
  saveShortcuts,
  selectMode,
  sendChat,
  sendShortcut,
  shortcuts,
  plugins,
  pluginsLoaded,
  startStream,
  stopStream
} = usePocketStation();

onMounted(() => {
  loadInitial();
  loadShortcuts();
  connectWs();
});

watch(activeTab, (tab) => {
  if (tab === "state") {
    loadPlugins();
  }
});

function applyCommand(command: string) {
  chatDraft.value = command;
  activeTab.value = "chat";
}

function doSendShortcut(command: string) {
  sendShortcut(command);
  activeTab.value = "chat";
}
</script>

<template>
  <main class="app">
    <TopBar
      :connection-mode="connectionMode"
      :connection-text="connectionText"
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
      @start="startStream"
      @stop="stopStream"
    />

    <CommandsView
      :class="{ active: activeTab === 'commands' }"
      :server-info="serverInfo"
      @command="applyCommand"
    />
  </main>

  <BottomTabs v-model:active-tab="activeTab" />
</template>
