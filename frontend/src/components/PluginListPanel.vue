<script setup lang="ts">
import { ref, computed } from "vue";
import type { CommandResult, PluginInfo } from "@/types";

const props = defineProps<{
  plugins: PluginInfo[];
  pluginsLoaded: boolean;
  togglePlugin: (internalName: string, enable: boolean) => Promise<CommandResult>;
}>();

const emit = defineEmits<{
  "refreshPlugins": [];
}>();

const expanded = ref(true);
const search = ref("");
const confirming = ref<{ internalName: string; name: string; enable: boolean } | null>(null);
const toggling = ref("");

const filteredPlugins = computed(() => {
  const q = search.value.trim().toLowerCase();
  if (!q) return props.plugins;
  return props.plugins.filter((p) =>
    p.name.toLowerCase().includes(q) || p.internalName.toLowerCase().includes(q)
  );
});

async function doToggle(internalName: string, name: string, enable: boolean) {
  toggling.value = internalName;
  try {
    const result = await props.togglePlugin(internalName, enable);
    if (!result.ok) {
      alert(result.message || "操作失败");
    }
    emit("refreshPlugins");
  } catch (e) {
    alert(String(e));
  } finally {
    toggling.value = "";
    confirming.value = null;
  }
}
</script>

<template>
  <div class="collapsible-panel">
    <div class="section-title collapsible-header" @click="expanded = !expanded">
      <span class="collapse-arrow">{{ expanded ? "▼" : "▶" }}</span>
      Dalamud 插件开关
    </div>

    <template v-if="expanded">
      <div class="search-row">
        <input
          v-model="search"
          type="text"
          placeholder="搜索插件..."
          class="search-input"
        />
      </div>

      <div v-if="!pluginsLoaded" class="empty">加载中...</div>

      <div v-else class="scroll-list">
        <div
          v-for="plugin in filteredPlugins"
          :key="plugin.internalName"
          class="plugin-item"
        >
          <div class="plugin-info">
            <span class="plugin-name">{{ plugin.name }}</span>
            <span class="plugin-version">{{ plugin.version }}</span>
          </div>
          <div class="plugin-actions">
            <span v-if="plugin.isLoaded" class="badge badge-on">已启用</span>
            <span v-else class="badge badge-off">已禁用</span>
            <button
              v-if="plugin.isLoaded"
              class="btn-toggle btn-disable"
              :disabled="toggling === plugin.internalName"
              @click="confirming = { internalName: plugin.internalName, name: plugin.name, enable: false }"
            >禁用</button>
            <button
              v-else
              class="btn-toggle btn-enable"
              :disabled="toggling === plugin.internalName"
              @click="confirming = { internalName: plugin.internalName, name: plugin.name, enable: true }"
            >启用</button>
          </div>
        </div>
        <div v-if="filteredPlugins.length === 0" class="empty">
          {{ search ? "无匹配插件" : "无已安装插件" }}
        </div>
      </div>
    </template>
  </div>

  <div v-if="confirming" class="overlay" @click.self="confirming = null">
    <div class="confirm-box">
      <p>
        确定要{{ confirming.enable ? '启用' : '禁用' }}
        <strong>{{ confirming.name }}</strong>？
      </p>
      <div class="confirm-actions">
        <button class="btn-toggle btn-cancel" @click="confirming = null">取消</button>
        <button
          class="btn-toggle"
          :class="confirming.enable ? 'btn-enable' : 'btn-disable'"
          @click="doToggle(confirming.internalName, confirming.name, confirming.enable)"
        >确定</button>
      </div>
    </div>
  </div>
</template>
