import { ref } from "vue";
import { getJson } from "@/services/pocketApi";
import type { PlayerSnapshot } from "@/types";

export function usePlayerState() {
  const snapshot = ref<PlayerSnapshot | null>(null);

  function setSnapshot(value: PlayerSnapshot | null | undefined) {
    snapshot.value = value ?? null;
  }

  async function loadState() {
    const state = await getJson<PlayerSnapshot>("/api/state");
    setSnapshot(state);
    return state;
  }

  return {
    snapshot,
    loadState,
    setSnapshot
  };
}
