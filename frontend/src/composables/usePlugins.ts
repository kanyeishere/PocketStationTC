import { ref } from "vue";
import { getJson, postJson } from "@/services/pocketApi";
import type { CommandResult, PluginInfo } from "@/types";

export function usePlugins() {
  const plugins = ref<PluginInfo[]>([]);
  const pluginsLoaded = ref(false);

  async function loadPlugins() {
    try {
      plugins.value = await getJson<PluginInfo[]>("/api/plugins");
    } catch {
      plugins.value = [];
    } finally {
      pluginsLoaded.value = true;
    }
  }

  async function togglePlugin(internalName: string, enable: boolean): Promise<CommandResult> {
    const action = enable ? "enable" : "disable";
    return await postJson<CommandResult>(`/api/plugins/${encodeURIComponent(internalName)}/${action}`, {});
  }

  return {
    plugins,
    pluginsLoaded,
    loadPlugins,
    togglePlugin
  };
}
