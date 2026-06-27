import { ref } from "vue";
import { getJson, postJson } from "@/services/pocketApi";
import type { CommandShortcut } from "@/types";

export function useShortcuts() {
  const shortcuts = ref<CommandShortcut[]>([]);

  async function loadShortcuts() {
    try {
      shortcuts.value = await getJson<CommandShortcut[]>("/api/shortcuts");
    } catch {
      shortcuts.value = [];
    }
  }

  async function saveShortcuts(list: CommandShortcut[]) {
    await postJson<CommandShortcut[]>("/api/shortcuts", list);
    shortcuts.value = list;
  }

  return {
    shortcuts,
    loadShortcuts,
    saveShortcuts
  };
}
