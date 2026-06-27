import { computed, ref } from "vue";
import { imageApiUrl, postJson } from "@/services/pocketApi";
import type { CommandResult, ConnectionMode, ScreenshotReadyEvent } from "@/types";

type SetConnection = (text: string, mode?: ConnectionMode) => void;

export function useScreenCapture(setConnection: SetConnection) {
  const screenshot = ref<ScreenshotReadyEvent | null>(null);
  const screenMeta = ref("");
  const screenshotLoading = ref(false);

  const screenImageUrl = computed(() => {
    if (!screenshot.value) {
      return "";
    }

    return imageApiUrl(screenshot.value.url || "/api/screen/latest.jpg");
  });

  function renderScreenshot(payload: ScreenshotReadyEvent) {
    screenshot.value = payload;
    screenMeta.value = `${payload.width} x ${payload.height}`;
  }

  function setScreenError(message: string) {
    screenMeta.value = message;
  }

  async function requestScreenshot() {
    screenshotLoading.value = true;
    screenMeta.value = "请求中";

    try {
      const result = await postJson<CommandResult<ScreenshotReadyEvent>>("/api/screen/capture", {});
      const data = result.data as Partial<ScreenshotReadyEvent> | undefined;
      if (result.ok && data && typeof data.url === "string") {
        renderScreenshot(data as ScreenshotReadyEvent);
      } else if (!result.ok) {
        const message = result.message || "命令失败";
        setConnection(message, "offline");
        setScreenError(message);
      }
    } catch (error) {
      const message = String(error);
      setConnection(message, "offline");
      setScreenError(message);
    } finally {
      screenshotLoading.value = false;
    }
  }

  return {
    screenImageUrl,
    screenMeta,
    screenshot,
    screenshotLoading,
    renderScreenshot,
    requestScreenshot,
    setScreenError
  };
}
