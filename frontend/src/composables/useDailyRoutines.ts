import { ref } from "vue";
import { getJson } from "@/services/pocketApi";
import type { DailyRoutinesModule } from "@/types";

type SendChat = (content: string, channel: string) => Promise<boolean>;

export function useDailyRoutines(sendChat: SendChat) {
  const dailyRoutinesModules = ref<DailyRoutinesModule[]>([]);
  const dailyRoutinesLoading = ref(false);

  async function loadDailyRoutines() {
    dailyRoutinesLoading.value = true;
    try {
      const data = await getJson<{ modules: DailyRoutinesModule[] }>("/api/dailyroutines");
      dailyRoutinesModules.value = data.modules || [];
    } catch {
      dailyRoutinesModules.value = [];
    } finally {
      dailyRoutinesLoading.value = false;
    }
  }

  async function toggleDailyRoutine(name: string, enable: boolean) {
    const command = enable ? `/pdr load ${name}` : `/pdr unload ${name}`;
    const sent = await sendChat(command, "");
    if (!sent) {
      return false;
    }

    const mod = dailyRoutinesModules.value.find((m) => m.name === name);
    if (mod) {
      mod.enabled = enable;
    }

    return true;
  }

  return {
    dailyRoutinesLoading,
    dailyRoutinesModules,
    loadDailyRoutines,
    toggleDailyRoutine
  };
}
