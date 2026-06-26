<script setup lang="ts">
import CharacterPanel from "@/components/CharacterPanel.vue";
import type { PlayerSnapshot, PluginInfo } from "@/types";

defineProps<{
  snapshot: PlayerSnapshot | null;
  plugins: PluginInfo[];
  pluginsLoaded: boolean;
}>();
</script>

<template>
  <section id="tab-state" class="view">
    <div class="section-title">自身</div>
    <CharacterPanel :character="snapshot?.localPlayer" empty-text="未登录" />

    <div class="section-title">目标</div>
    <CharacterPanel :character="snapshot?.target" empty-text="无目标" />

    <div class="section-title">小队</div>
    <div class="list">
      <CharacterPanel
        v-for="member in snapshot?.party || []"
        :key="`${member.objectId}-${member.entityId}`"
        :character="member"
        empty-text="未知成员"
      />
    </div>

    <div class="section-title">已启用插件</div>
    <div v-if="!pluginsLoaded" class="empty">加载中...</div>
    <div v-else-if="plugins.filter(p => p.isLoaded).length === 0" class="empty">无已启用插件</div>
    <div v-else class="plugin-list">
      <div
        v-for="plugin in plugins.filter(p => p.isLoaded)"
        :key="plugin.internalName"
        class="plugin-item"
      >
        <span class="plugin-name">{{ plugin.name }}</span>
        <span class="plugin-version">{{ plugin.version }}</span>
      </div>
    </div>
  </section>
</template>
