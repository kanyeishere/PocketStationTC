<script setup lang="ts">
import { computed, nextTick, ref, watch } from "vue";
import ChatFilterEditor from "@/components/ChatFilterEditor.vue";
import type { ChatEvent, ChatFilterMode, ChatTypeOption } from "@/types";

const props = defineProps<{
  allChatTypes: ChatTypeOption[];
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

const defaultChatColor: Rgb = [204, 204, 204];
const chatTypeColors: Record<string, Rgb> = {
  debug: [204, 204, 204],
  urgent: [255, 127, 127],
  notice: [179, 140, 255],
  say: [247, 247, 247],
  gmsay: [247, 247, 247],
  shout: [255, 166, 102],
  gmshout: [255, 166, 102],
  tellincoming: [255, 184, 222],
  telloutgoing: [255, 184, 222],
  gmtell: [255, 184, 222],
  party: [102, 229, 255],
  crossparty: [102, 229, 255],
  gmparty: [102, 229, 255],
  alliance: [255, 127, 0],
  novicenetwork: [212, 255, 125],
  novicenetworksystem: [212, 255, 125],
  gmnovicenetwork: [212, 255, 125],
  standardemote: [186, 255, 240],
  customemote: [186, 255, 240],
  yell: [255, 255, 0],
  gmyell: [255, 255, 0],
  echo: [204, 204, 204],
  systemmessage: [204, 204, 204],
  systemerror: [255, 74, 74],
  system: [204, 204, 204],
  battlesystem: [204, 204, 204],
  gatheringsystemmessage: [204, 204, 204],
  gatheringsystem: [204, 204, 204],
  periodicrecruitmentnotification: [204, 204, 204],
  orchestrion: [204, 204, 204],
  alarm: [204, 204, 204],
  glamournotifications: [204, 204, 204],
  retainersale: [204, 204, 204],
  sign: [204, 204, 204],
  messagebook: [204, 204, 204],
  npcdialogue: [171, 214, 71],
  npcdialogueannouncements: [171, 214, 71],
  npcannouncement: [171, 214, 71],
  errormessage: [255, 74, 74],
  error: [255, 74, 74],
  freecompany: [171, 219, 229],
  freecompanyannouncement: [171, 219, 229],
  freecompanyloginlogout: [171, 219, 229],
  gmfreecompany: [171, 219, 229],
  pvpteam: [171, 219, 229],
  pvpteamannouncement: [171, 219, 229],
  pvpteamloginlogout: [171, 219, 229],
  action: [255, 255, 176],
  item: [255, 255, 176],
  lootnotice: [255, 255, 176],
  progress: [255, 222, 115],
  lootroll: [199, 191, 158],
  randomnumber: [199, 191, 158],
  crafting: [222, 191, 247],
  gathering: [222, 191, 247],
  damage: [255, 125, 125],
  miss: [204, 204, 204],
  healing: [212, 255, 125],
  gainbuff: [148, 191, 255],
  losebuff: [148, 191, 255],
  gaindebuff: [255, 138, 196],
  losedebuff: [255, 138, 196]
};

const linkshellColor: Rgb = [212, 255, 125];

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

function chatRowStyle(type: string) {
  const [red, green, blue] = getChatColor(type);
  return {
    "--chat-color": `rgb(${red} ${green} ${blue})`,
    "--chat-color-soft": `rgb(${red} ${green} ${blue} / 0.18)`,
    "--chat-color-faint": `rgb(${red} ${green} ${blue} / 0.09)`,
    "--chat-color-shadow": `rgb(${red} ${green} ${blue} / 0.38)`
  };
}

function getChatColor(type: string): Rgb {
  const key = normalizeChatType(type);
  if (/^(cross)?linkshell\d+$/.test(key) || /^ls\d+$/.test(key) || /^gmlinkshell\d+$/.test(key)) {
    return linkshellColor;
  }

  return chatTypeColors[key] || defaultChatColor;
}

function normalizeChatType(type: string) {
  return (type || "")
    .replace(/[\s_-]+/g, "")
    .toLocaleLowerCase();
}

type Rgb = [number, number, number];
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
      <div
        v-for="item in chats"
        :key="`${item.sequence}-${item.timestamp}`"
        class="chat-row"
        :style="chatRowStyle(item.channel)"
      >
        <div class="chat-meta">
          <span class="chat-channel">{{ item.channel || "" }}</span>
          <span v-if="item.sender" class="chat-sender">{{ item.sender }}</span>
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
