<script setup lang="ts">
import type { TabKey } from "@/types";

const tabs: Array<{ key: TabKey; label: string }> = [
  { key: "chat", label: "聊天" },
  { key: "state", label: "状态" },
  { key: "screen", label: "画面" },
  { key: "commands", label: "指令" }
];

defineProps<{
  activeTab: TabKey;
}>();

const emit = defineEmits<{
  "update:activeTab": [tab: TabKey];
}>();
</script>

<template>
  <nav class="bottom-tabs">
    <button
      v-for="tab in tabs"
      :key="tab.key"
      type="button"
      :class="{ active: activeTab === tab.key }"
      @click="emit('update:activeTab', tab.key)"
    >
      {{ tab.label }}
    </button>
  </nav>
</template>
