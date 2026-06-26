<script setup lang="ts">
import { ref, computed } from "vue";
import type { DailyRoutinesModule } from "@/types";

const props = defineProps<{
  modules: DailyRoutinesModule[];
  loading: boolean;
}>();

const emit = defineEmits<{
  toggle: [name: string, enable: boolean];
}>();

const expanded = ref(true);
const search = ref("");
const confirming = ref<{ name: string; display: string; enable: boolean } | null>(null);

const filteredModules = computed(() => {
  const q = search.value.trim().toLowerCase();
  if (!q) return props.modules;
  return props.modules.filter((m) => {
    const display = (m.displayName || m.name).toLowerCase();
    return display.includes(q) || m.name.toLowerCase().includes(q);
  });
});

function displayName(mod: DailyRoutinesModule) {
  return mod.displayName || mod.name;
}

function doConfirm(mod: DailyRoutinesModule) {
  confirming.value = {
    name: mod.name,
    display: displayName(mod),
    enable: !mod.enabled
  };
}

function doToggle() {
  if (!confirming.value) return;
  emit("toggle", confirming.value.name, confirming.value.enable);
  confirming.value = null;
}
</script>

<template>
  <div class="collapsible-panel">
    <div class="section-title collapsible-header" @click="expanded = !expanded">
      <span class="collapse-arrow">{{ expanded ? "▼" : "▶" }}</span>
      Daily Routines 模块开关
    </div>

    <template v-if="expanded">
      <div class="search-row">
        <input
          v-model="search"
          type="text"
          placeholder="搜索模块..."
          class="search-input"
        />
      </div>

      <div v-if="loading" class="empty">加载中...</div>

      <div v-else class="scroll-list">
        <div
          v-for="mod in filteredModules"
          :key="mod.name"
          class="plugin-item"
        >
          <div class="plugin-info">
            <span class="plugin-name">{{ displayName(mod) }}</span>
            <span class="plugin-version">{{ mod.name }}</span>
          </div>
          <div class="plugin-actions">
            <span v-if="mod.enabled" class="badge badge-on">已启用</span>
            <span v-else class="badge badge-off">已禁用</span>
            <button
              v-if="mod.enabled"
              class="btn-toggle btn-disable"
              @click="doConfirm(mod)"
            >禁用</button>
            <button
              v-else
              class="btn-toggle btn-enable"
              @click="doConfirm(mod)"
            >启用</button>
          </div>
        </div>
        <div v-if="filteredModules.length === 0" class="empty">
          {{ search ? "无匹配模块" : "无模块数据" }}
        </div>
      </div>
    </template>

    <div v-if="confirming" class="overlay" @click.self="confirming = null">
      <div class="confirm-box">
        <p>
          确定要{{ confirming.enable ? '启用' : '禁用' }}
          <strong>{{ confirming.display }}</strong>？
        </p>
        <div class="confirm-actions">
          <button class="btn-toggle btn-cancel" @click="confirming = null">取消</button>
          <button
            class="btn-toggle"
            :class="confirming.enable ? 'btn-enable' : 'btn-disable'"
            @click="doToggle"
          >确定</button>
        </div>
      </div>
    </div>
  </div>
</template>
