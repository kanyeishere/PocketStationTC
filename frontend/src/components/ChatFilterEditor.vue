<script setup lang="ts">
import { ref, watch } from "vue";
import type { ChatFilterMode } from "@/types";

const props = defineProps<{
  allTypes: string[];
  currentMode: ChatFilterMode;
  shownCount: number;
  totalCount: number;
}>();

const emit = defineEmits<{
  save: [mode: ChatFilterMode];
  delete: [];
}>();

const modeName = ref("");
const includeText = ref("");
const excludeText = ref("");
const selectedTypes = ref<string[]>([]);

watch(
  () => props.currentMode,
  (mode) => {
    modeName.value = mode.name || "";
    includeText.value = (mode.includeKeywords || []).join("\n");
    excludeText.value = (mode.excludeKeywords || []).join("\n");
    selectedTypes.value = [...(mode.enabledTypes || [])];
  },
  { immediate: true, deep: true }
);

function save(copy = false) {
  const mode: ChatFilterMode = {
    id: copy || props.currentMode.isBuiltIn ? `custom-${Date.now()}` : props.currentMode.id,
    name: modeName.value.trim() || "未命名模式",
    isBuiltIn: false,
    enabledTypes: [...selectedTypes.value],
    includeKeywords: parseList(includeText.value),
    excludeKeywords: parseList(excludeText.value)
  };

  emit("save", mode);
}

function toggleType(type: string) {
  const index = selectedTypes.value.findIndex((item) => equalsText(item, type));
  if (index >= 0) {
    selectedTypes.value.splice(index, 1);
    return;
  }

  selectedTypes.value.push(type);
}

function isSelected(type: string) {
  return selectedTypes.value.some((item) => equalsText(item, type));
}

function parseList(value: string) {
  return value
    .split(/[\n,，]+/)
    .map((item) => item.trim())
    .filter((item, index, array) => item && array.findIndex((other) => equalsText(other, item)) === index);
}

function equalsText(left: string, right: string) {
  return left.toLocaleLowerCase() === right.toLocaleLowerCase();
}
</script>

<template>
  <div class="filter-editor">
    <input v-model="modeName" autocomplete="off" placeholder="模式名称">

    <details class="type-details">
      <summary>消息类型</summary>
      <div class="type-palette">
        <button
          v-for="type in allTypes"
          :key="type"
          class="type-chip"
          :class="{ active: isSelected(type) }"
          type="button"
          @click="toggleType(type)"
        >
          {{ type }}
        </button>
      </div>
    </details>

    <div class="keyword-grid">
      <textarea v-model="includeText" rows="2" placeholder="包含关键词" />
      <textarea v-model="excludeText" rows="2" placeholder="排除关键词" />
    </div>

    <div class="editor-actions">
      <button type="button" @click="save(false)">{{ currentMode.isBuiltIn ? "另存" : "保存" }}</button>
      <button type="button" @click="save(true)">另存</button>
      <button type="button" :disabled="currentMode.isBuiltIn" @click="emit('delete')">删除</button>
      <span>{{ shownCount }}/{{ totalCount }}</span>
    </div>
  </div>
</template>
