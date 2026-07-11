import { onBeforeUnmount, shallowRef } from "vue";
import { createEnvelope, postJson, websocketUrl } from "@/services/pocketApi";
import type { CommandResult, ConnectionMode, Envelope } from "@/types";

type SetConnection = (text: string, mode?: ConnectionMode) => void;

interface PocketConnectionHandlers {
  onBinaryFrame: (data: ArrayBuffer) => void | Promise<void>;
  onEnvelope: (envelope: Envelope) => void;
  onOpen?: () => void | Promise<void>;
}

export function usePocketConnection(
  setConnection: SetConnection,
  handlers: PocketConnectionHandlers
) {
  const ws = shallowRef<WebSocket | null>(null);
  let reconnectTimer: number | undefined;
  let shouldReconnect = true;

  function connectWs(force = false) {
    shouldReconnect = true;

    if (!force && ws.value && ws.value.readyState < WebSocket.CLOSING) {
      return;
    }

    window.clearTimeout(reconnectTimer);
    reconnectTimer = undefined;

    if (force) {
      closeSocket(ws.value);
    }

    const socket = new WebSocket(websocketUrl());
    socket.binaryType = "arraybuffer";
    ws.value = socket;
    setConnection("连接中");

    socket.onopen = () => {
      if (ws.value !== socket) {
        closeSocket(socket);
        return;
      }

      setConnection("已连接", "online");
      void handlers.onOpen?.();
    };
    socket.onmessage = (event) => {
      if (ws.value !== socket) {
        return;
      }

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
      if (ws.value === socket) {
        ws.value = null;
      }

      if (!shouldReconnect) {
        return;
      }

      setConnection("已断开，重连中", "offline");
      window.clearTimeout(reconnectTimer);
      reconnectTimer = window.setTimeout(connectWs, 1500);
    };
    socket.onerror = () => {
      if (ws.value === socket) {
        setConnection("连接错误", "offline");
      }
    };
  }

  function closeWs() {
    shouldReconnect = false;
    window.clearTimeout(reconnectTimer);
    reconnectTimer = undefined;
    closeSocket(ws.value);
    ws.value = null;
  }

  function reconnectWs() {
    connectWs(true);
  }

  function closeSocket(socket: WebSocket | null) {
    if (!socket) {
      return;
    }

    socket.onopen = null;
    socket.onmessage = null;
    socket.onclose = null;
    socket.onerror = null;

    if (socket.readyState < WebSocket.CLOSING) {
      socket.close();
    }
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
    reconnectWs,
    sendEnvelope
  };
}
