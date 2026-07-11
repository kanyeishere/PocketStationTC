import { onBeforeUnmount, ref, shallowRef } from "vue";
import { getJson, postCommand } from "@/services/pocketApi";
import type { StreamConfig } from "@/types";

type SendEnvelope = (type: string, payload?: unknown) => Promise<void>;

const firstFrameTimeoutMs = 4500;
const recoverCooldownMs = 3500;

export function useLiveStream(_sendEnvelope: SendEnvelope) {
  const liveFps = ref(30);
  const liveRunning = ref(false);
  const liveFrame = shallowRef<ImageBitmap | null>(null);
  const liveFrameSize = ref("");
  const liveWaiting = ref(false);

  let frameSequence = 0;
  let lastFrameAt = 0;
  let watchdogTimer: number | undefined;
  let recoverInFlight = false;
  let lastRecoverAt = 0;

  async function handleBinaryFrame(data: ArrayBuffer) {
    const sequence = ++frameSequence;

    try {
      const blob = new Blob([data], { type: "image/jpeg" });
      const bitmap = await createImageBitmap(blob);

      if (sequence !== frameSequence) {
        bitmap.close();
        return;
      }

      liveFrame.value = bitmap;
      liveFrameSize.value = `${bitmap.width} x ${bitmap.height}`;
      liveRunning.value = true;
      liveWaiting.value = false;
      lastFrameAt = Date.now();
      scheduleFrameWatchdog();
    } catch {
      // Corrupt frames are expected occasionally during stream transitions.
    }
  }

  async function startStream(fps?: number) {
    const targetFps = fps ?? liveFps.value;
    liveWaiting.value = true;

    try {
      await requestStreamStart(targetFps);
    } catch (error) {
      liveRunning.value = false;
      liveWaiting.value = false;
      clearFrameWatchdog();
      throw error;
    }
  }

  async function stopStream() {
    try {
      await postCommand("/api/stream/stop", {});
    } finally {
      liveRunning.value = false;
      liveWaiting.value = false;
      clearFrameWatchdog();
      clearFrame();
    }
  }

  async function syncStreamConfig() {
    const config = await getJson<StreamConfig>("/api/stream/config");
    applyStreamConfig(config);
    return config;
  }

  async function recoverStreamIfStale(force = false) {
    if (recoverInFlight) {
      return;
    }

    let running = liveRunning.value;
    try {
      running = (await syncStreamConfig()).running;
    } catch {
      // Keep the local state and try a lightweight recovery below.
    }

    if (!running) {
      return;
    }

    const now = Date.now();
    const hasRecentFrame = lastFrameAt > 0 && now - lastFrameAt < firstFrameTimeoutMs;
    if (!force && hasRecentFrame) {
      scheduleFrameWatchdog();
      return;
    }

    if (now - lastRecoverAt < recoverCooldownMs) {
      scheduleFrameWatchdog();
      return;
    }

    recoverInFlight = true;
    lastRecoverAt = now;
    liveWaiting.value = true;

    try {
      await requestStreamStart(liveFps.value);
    } finally {
      recoverInFlight = false;
    }
  }

  async function requestStreamStart(fps: number) {
    await postCommand("/api/stream/start", { fps });
    liveFps.value = fps;
    liveRunning.value = true;
    liveWaiting.value = !liveFrame.value;
    scheduleFrameWatchdog();
  }

  function applyStreamConfig(config: StreamConfig) {
    liveFps.value = config.fps;
    liveRunning.value = config.running;

    if (!config.running) {
      liveWaiting.value = false;
      clearFrameWatchdog();
      clearFrame();
      return;
    }

    liveWaiting.value = !liveFrame.value;
    scheduleFrameWatchdog();
  }

  function scheduleFrameWatchdog() {
    clearFrameWatchdog();

    if (!liveRunning.value) {
      return;
    }

    watchdogTimer = window.setTimeout(() => {
      void recoverStreamIfStale();
    }, firstFrameTimeoutMs);
  }

  function clearFrameWatchdog() {
    window.clearTimeout(watchdogTimer);
    watchdogTimer = undefined;
  }

  function clearFrame() {
    liveFrame.value = null;
    liveFrameSize.value = "";
    lastFrameAt = 0;
  }

  onBeforeUnmount(() => {
    clearFrameWatchdog();
    clearFrame();
  });

  return {
    liveFps,
    liveFrame,
    liveFrameSize,
    liveRunning,
    liveWaiting,
    applyStreamConfig,
    handleBinaryFrame,
    recoverStreamIfStale,
    startStream,
    stopStream,
    syncStreamConfig
  };
}
