<script setup lang="ts">
import { onMounted, ref } from "vue";
import BottomTabs from "@/components/BottomTabs.vue";
import TopBar from "@/components/TopBar.vue";
import { usePocketStation } from "@/composables/usePocketStation";
import ChatView from "@/views/ChatView.vue";
import CommandsView from "@/views/CommandsView.vue";
import ScreenView from "@/views/ScreenView.vue";
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
  screenImageUrl,
  screenMeta,
  screenshotLoading,
  sendLoading,
  serverInfo,
  snapshot,
  connectWs,
  deleteCurrentMode,
  loadInitial,
  requestScreenshot,
  saveMode,
  selectMode,
  sendChat
} = usePocketStation();

onMounted(() => {
  loadInitial();
  connectWs();
});

function applyCommand(command: string) {
  chatDraft.value = command;
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
    />

    <ScreenView
      :class="{ active: activeTab === 'screen' }"
      :image-url="screenImageUrl"
      :loading="screenshotLoading"
      :meta="screenMeta"
      @capture="requestScreenshot"
    />

    <CommandsView
      :class="{ active: activeTab === 'commands' }"
      :server-info="serverInfo"
      @command="applyCommand"
    />
  </main>

  <BottomTabs v-model:active-tab="activeTab" />
</template>
