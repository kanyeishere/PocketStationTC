import { onBeforeUnmount, shallowRef } from "vue";
import { createEnvelope, postJson, websocketUrl } from "@/services/pocketApi";
import type { CommandResult, ConnectionMode, Envelope } from "@/types";

type SetConnection = (text: string, mode?: ConnectionMode) => void;

interface PocketConnectionHandlers {
  onBinaryFrame: (data: ArrayBuffer) => void | Promise<void>;
  onEnvelope: (envelope: Envelope) => void;
}

export function usePocketConnection(
  setConnection: SetConnection,
  handlers: PocketConnectionHandlers
) {
  const ws = shallowRef<WebSocket | null>(null);
  let reconnectTimer: number | undefined;

  function connectWs() {
    if (ws.value && ws.value.readyState < WebSocket.CLOSING) {
      return;
    }

    const socket = new WebSocket(websocketUrl());
    socket.binaryType = "arraybuffer";
    ws.value = socket;
    setConnection("连接中");

    socket.onopen = () => setConnection("已连接", "online");
    socket.onmessage = (event) => {
      if (event.data instanceof ArrayBuffer) {
        void handlers.onBinaryFrame(event.data);
        return;
      }

      try {
        handlers.onEnvelope(JSON.parse(event.data) as Envelope);
      } catch (error) {
        setConnection(String(error), "offline");
      }
    };
    socket.onclose = () => {
      setConnection("已断开，重连中", "offline");
      window.clearTimeout(reconnectTimer);
      reconnectTimer = window.setTimeout(connectWs, 1500);
    };
    socket.onerror = () => setConnection("连接错误", "offline");
  }

  function closeWs() {
    window.clearTimeout(reconnectTimer);
    reconnectTimer = undefined;
    ws.value?.close();
    ws.value = null;
  }

  async function sendEnvelope(type: string, payload: unknown = {}) {
    const envelope = createEnvelope(type, payload);
    if (ws.value?.readyState === WebSocket.OPEN) {
      ws.value.send(JSON.stringify(envelope));
      return;
    }

    await postJson<CommandResult>("/api/command", envelope);
  }

  onBeforeUnmount(closeWs);

  return {
    closeWs,
    connectWs,
    sendEnvelope
  };
}
