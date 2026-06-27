import { ref } from "vue";
import type { ConnectionMode } from "@/types";

export function useConnectionStatus() {
  const connectionText = ref("连接中");
  const connectionMode = ref<ConnectionMode>("");

  function setConnection(text: string, mode: ConnectionMode = "") {
    connectionText.value = text;
    connectionMode.value = mode;
  }

  return {
    connectionMode,
    connectionText,
    setConnection
  };
}
