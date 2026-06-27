import { ref, shallowRef } from "vue";

type SendEnvelope = (type: string, payload?: unknown) => Promise<void>;

export function useLiveStream(sendEnvelope: SendEnvelope) {
  const liveFps = ref(30);
  const liveRunning = ref(false);
  const liveFrame = shallowRef<ImageBitmap | null>(null);
  const liveFrameSize = ref("");

  async function handleBinaryFrame(data: ArrayBuffer) {
    try {
      const blob = new Blob([data], { type: "image/jpeg" });
      const bitmap = await createImageBitmap(blob);
      liveFrame.value = bitmap;
      liveFrameSize.value = `${bitmap.width} x ${bitmap.height}`;
    } catch {
      // Corrupt frames are expected occasionally during stream transitions.
    }
  }

  async function startStream(fps?: number) {
    const targetFps = fps ?? liveFps.value;
    try {
      await sendEnvelope("cmd.startStream", { fps: targetFps });
      liveFps.value = targetFps;
      liveRunning.value = true;
    } catch (error) {
      liveRunning.value = false;
      throw error;
    }
  }

  async function stopStream() {
    try {
      await sendEnvelope("cmd.stopStream", {});
    } finally {
      liveRunning.value = false;
      liveFrame.value = null;
    }
  }

  return {
    liveFps,
    liveFrame,
    liveFrameSize,
    liveRunning,
    handleBinaryFrame,
    startStream,
    stopStream
  };
}
