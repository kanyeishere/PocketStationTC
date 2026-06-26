<script setup lang="ts">
import { computed, nextTick, ref, watch } from "vue";
import ChatFilterEditor from "@/components/ChatFilterEditor.vue";
import type { ChatEvent, ChatFilterMode } from "@/types";

const props = defineProps<{
  allChatTypes: string[];
  chats: ChatEvent[];
  currentMode: ChatFilterMode;
  currentModeId: string;
  draft: string;
  filterModes: ChatFilterMode[];
  sendLoading: boolean;
  totalChatCount: number;
  deleteCurrentMode: () => Promise<void>;
  saveMode: (mode: ChatFilterMode) => Promise<void>;
  selectMode: (modeId: string) => Promise<void>;
  sendChat: (content: string, channel: string) => Promise<boolean>;
}>();

const emit = defineEmits<{
  "update:draft": [value: string];
}>();

const editorOpen = ref(false);
const channel = ref("");
const listRef = ref<HTMLElement | null>(null);

const draftValue = computed({
  get: () => props.draft,
  set: (value: string) => emit("update:draft", value)
});

watch(
  () => props.chats.length,
  async () => {
    await nextTick();
    scrollChat();
  }
);

async function submitChat() {
  const content = draftValue.value.trim();
  if (!content) {
    return;
  }

  const sent = await props.sendChat(content, channel.value);
  if (sent) {
    draftValue.value = "";
  }
}

async function selectMode(event: Event) {
  const value = (event.target as HTMLSelectElement).value;
  await props.selectMode(value);
}

function scrollChat() {
  const list = listRef.value;
  if (list) {
    list.scrollTop = list.scrollHeight;
  }
}
</script>

<template>
  <section id="tab-chat" class="view">
    <div class="chat-filter-bar">
      <select class="mode-select" :value="currentModeId" aria-label="聊天筛选模式" @change="selectMode">
        <option v-for="mode in filterModes" :key="mode.id" :value="mode.id">
          {{ mode.name || mode.id }}
        </option>
      </select>
      <button type="button" @click="editorOpen = !editorOpen">筛选</button>
    </div>

    <ChatFilterEditor
      v-if="editorOpen"
      :all-types="allChatTypes"
      :current-mode="currentMode"
      :shown-count="chats.length"
      :total-count="totalChatCount"
      @delete="deleteCurrentMode"
      @save="saveMode"
    />

    <div ref="listRef" class="chat-list">
      <div v-for="item in chats" :key="`${item.sequence}-${item.timestamp}`" class="chat-row">
        <div class="chat-meta">
          <span>{{ item.channel || "" }}</span>
          <span>{{ item.sender || "" }}</span>
        </div>
        <div class="chat-message">{{ item.message || "" }}</div>
      </div>
    </div>

    <form class="composer" @submit.prevent="submitChat">
      <select v-model="channel" class="channel-select" aria-label="发送频道">
        <option value="">当前</option>
        <option value="/s">说话</option>
        <option value="/p">小队</option>
        <option value="/fc">部队</option>
        <option value="/r">回复</option>
        <option value="/l">通讯贝</option>
        <option value="/cwl">跨服贝</option>
        <option value="/e">仅自己</option>
      </select>
      <input v-model="draftValue" autocomplete="off" placeholder="输入聊天内容或任意指令">
      <button type="submit" :disabled="sendLoading">发送</button>
    </form>
  </section>
</template>
