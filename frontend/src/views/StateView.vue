<script setup lang="ts">
import SelfPanel from "@/components/SelfPanel.vue";
import PluginListPanel from "@/components/PluginListPanel.vue";
import DailyRoutinesPanel from "@/components/DailyRoutinesPanel.vue";
import type { CommandResult, DailyRoutinesModule, PlayerSnapshot, PluginInfo } from "@/types";

defineProps<{
  snapshot: PlayerSnapshot | null;
  plugins: PluginInfo[];
  pluginsLoaded: boolean;
  togglePlugin: (internalName: string, enable: boolean) => Promise<CommandResult>;
  dailyRoutinesModules: DailyRoutinesModule[];
  dailyRoutinesLoading: boolean;
  toggleDailyRoutine: (name: string, enable: boolean) => Promise<boolean>;
}>();

defineEmits<{
  "refreshPlugins": [];
}>();
</script>

<template>
  <section id="tab-state" class="view">
    <SelfPanel
      :character="snapshot?.localPlayer"
      :currencies="snapshot?.currencies"
      :territory-name="snapshot?.territoryName"
      :world-name="snapshot?.worldName"
      :data-center-name="snapshot?.dataCenterName"
    />
    <DailyRoutinesPanel
      :modules="dailyRoutinesModules"
      :loading="dailyRoutinesLoading"
      @toggle="toggleDailyRoutine"
    />
    <PluginListPanel
      :plugins="plugins"
      :plugins-loaded="pluginsLoaded"
      :toggle-plugin="togglePlugin"
      @refresh-plugins="$emit('refreshPlugins')"
    />
  </section>
</template>
