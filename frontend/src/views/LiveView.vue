<script setup lang="ts">
import { ref, watch, onMounted, onUnmounted } from "vue";

const props = defineProps<{
  isActive: boolean;
  fps: number;
  running: boolean;
  frame: ImageBitmap | null;
  frameSize: string;
  waiting: boolean;
}>();

const emit = defineEmits<{
  start: [fps: number];
  stop: [];
}>();

const canvas = ref<HTMLCanvasElement | null>(null);
const screenFrame = ref<HTMLDivElement | null>(null);
const fpsInput = ref(props.fps);
const isFullscreen = ref(false);
const isFallbackFullscreen = ref(false);
const fallbackViewportStyle = ref<Record<string, string>>({});
let animating = false;
let pendingBitmap: ImageBitmap | null = null;

type WebKitFullscreenDocument = Document & {
  webkitFullscreenElement?: Element | null;
  webkitExitFullscreen?: () => Promise<void> | void;
};

type WebKitFullscreenElement = HTMLElement & {
  webkitRequestFullscreen?: () => Promise<void> | void;
};

function getFullscreenElement() {
  const doc = document as WebKitFullscreenDocument;
  return document.fullscreenElement ?? doc.webkitFullscreenElement ?? null;
}

function isIosDevice() {
  return /iPad|iPhone|iPod/.test(navigator.userAgent)
    || (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1);
}

function waitForAnimationFrame() {
  return new Promise<void>((resolve) => {
    requestAnimationFrame(() => resolve());
  });
}

function onFullscreenChange() {
  isFullscreen.value = isFallbackFullscreen.value || getFullscreenElement() === screenFrame.value;
}

function onKeydown(e: KeyboardEvent) {
  if (e.key === "Escape" && isFallbackFullscreen.value) {
    void exitFullscreenMode();
  }
}

onMounted(() => {
  document.addEventListener("fullscreenchange", onFullscreenChange);
  document.addEventListener("webkitfullscreenchange", onFullscreenChange);
  document.addEventListener("keydown", onKeydown);
});

onUnmounted(() => {
  document.removeEventListener("fullscreenchange", onFullscreenChange);
  document.removeEventListener("webkitfullscreenchange", onFullscreenChange);
  document.removeEventListener("keydown", onKeydown);
  stopFallbackViewportTracking();
});

async function requestNativeFullscreen(el: HTMLElement) {
  if (isIosDevice()) return false;

  const fullscreenEl = el as WebKitFullscreenElement;
  const requestFullscreen = fullscreenEl.requestFullscreen ?? fullscreenEl.webkitRequestFullscreen;
  if (!requestFullscreen) return false;

  try {
    await requestFullscreen.call(fullscreenEl);
    await waitForAnimationFrame();
    await waitForAnimationFrame();
    if (getFullscreenElement() !== el) return false;

    isFallbackFullscreen.value = false;
    isFullscreen.value = true;
    return true;
  } catch {
    return false;
  }
}

async function exitNativeFullscreen() {
  if (!getFullscreenElement()) return;

  const doc = document as WebKitFullscreenDocument;
  const exitFullscreen = document.exitFullscreen ?? doc.webkitExitFullscreen;
  if (!exitFullscreen) return;

  try {
    await exitFullscreen.call(doc);
  } catch {
    // Browser may reject when fullscreen has already been dismissed.
  }
}

function updateFallbackViewport() {
  const viewport = window.visualViewport;
  fallbackViewportStyle.value = viewport
    ? {
        "--live-fallback-left": `${viewport.offsetLeft}px`,
        "--live-fallback-top": `${viewport.offsetTop}px`,
        "--live-fallback-width": `${viewport.width}px`,
        "--live-fallback-height": `${viewport.height}px`
      }
    : {};
}

function startFallbackViewportTracking() {
  updateFallbackViewport();
  window.visualViewport?.addEventListener("resize", updateFallbackViewport);
  window.visualViewport?.addEventListener("scroll", updateFallbackViewport);
  window.addEventListener("orientationchange", updateFallbackViewport);
}

function stopFallbackViewportTracking() {
  window.visualViewport?.removeEventListener("resize", updateFallbackViewport);
  window.visualViewport?.removeEventListener("scroll", updateFallbackViewport);
  window.removeEventListener("orientationchange", updateFallbackViewport);
  fallbackViewportStyle.value = {};
}

function enterFallbackFullscreen() {
  isFallbackFullscreen.value = true;
  startFallbackViewportTracking();
  onFullscreenChange();
}

function exitFallbackFullscreen() {
  isFallbackFullscreen.value = false;
  stopFallbackViewportTracking();
  onFullscreenChange();
}

async function enterFullscreenMode() {
  const el = screenFrame.value;
  if (!el) return;

  const enteredNativeFullscreen = await requestNativeFullscreen(el);
  if (enteredNativeFullscreen) return;

  enterFallbackFullscreen();
}

async function exitFullscreenMode() {
  if (getFullscreenElement()) {
    await exitNativeFullscreen();
  }

  exitFallbackFullscreen();
}

async function toggleFullscreen() {
  if (!props.running) return;
  if (isFullscreen.value) {
    await exitFullscreenMode();
  } else {
    await enterFullscreenMode();
  }
}

function clampFps() {
  const n = typeof fpsInput.value === "number" ? fpsInput.value : parseInt(String(fpsInput.value), 10);
  if (isNaN(n) || n < 1) {
    fpsInput.value = props.fps || 30;
  } else {
    fpsInput.value = Math.max(1, Math.min(120, n));
  }
}

function onFpsBlur() {
  const old = fpsInput.value;
  clampFps();
  if (props.running && fpsInput.value !== old) {
    emit("start", fpsInput.value);
  }
}

function onFpsKeydown(e: KeyboardEvent) {
  if (e.key === "Enter") {
    (e.target as HTMLInputElement).blur();
  }
}

function drawPendingFrame() {
  if (animating) return;

  animating = true;
  requestAnimationFrame(() => {
    const bitmap = pendingBitmap;
    pendingBitmap = null;
    animating = false;

    if (!bitmap || !canvas.value) {
      return;
    }

    drawFrame(bitmap);

    if (pendingBitmap) {
      drawPendingFrame();
    }
  });
}

function drawFrame(bitmap: ImageBitmap) {
  const c = canvas.value;
  if (!c) return;

  const ctx = c.getContext("2d");
  if (!ctx) return;

  if (c.width !== bitmap.width || c.height !== bitmap.height) {
    c.width = bitmap.width;
    c.height = bitmap.height;
  }

  ctx.drawImage(bitmap, 0, 0);
}

watch(() => props.frame, (bitmap) => {
  if (!bitmap) return;

  pendingBitmap = bitmap;
  drawPendingFrame();
});

watch(() => props.fps, (v) => {
  fpsInput.value = v;
});

function toggleStream() {
  if (props.running) {
    emit("stop");
  } else {
    clampFps();
    emit("start", fpsInput.value);
  }
}

watch(
  () => props.isActive,
  (active, _prev) => {
    if (active && !props.running) {
      // Auto-start stream when entering the tab
      emit("start", fpsInput.value);
    } else if (!active && props.running) {
      // Auto-stop stream when leaving the tab
      emit("stop");
    }

    if (!active && isFullscreen.value) {
      void exitFullscreenMode();
    }
  },
  { immediate: true }
);

watch(() => props.running, (running) => {
  if (!running && isFullscreen.value) {
    void exitFullscreenMode();
  }
});
</script>

<template>
  <section id="tab-live" class="view">
    <div class="screen-toolbar">
      <button type="button" @click="toggleStream">
        {{ running ? "停止直播" : "开始直播" }}
      </button>
      <label class="fps-label">
        FPS
        <input
          v-model.number="fpsInput"
          type="number"
          min="1"
          max="120"
          class="fps-input"
          @blur="onFpsBlur"
          @keydown="onFpsKeydown"
        >
      </label>
      <span>{{ running ? (waiting ? "● 等待画面" : `● ${frameSize}`) : "○ 未推流" }}</span>
    </div>
    <div
      ref="screenFrame"
      class="screen-frame"
      :class="{
        'is-fullscreen': isFullscreen,
        'is-fallback-fullscreen': isFallbackFullscreen,
        'can-fullscreen': running
      }"
      :style="isFallbackFullscreen ? fallbackViewportStyle : undefined"
      @click="toggleFullscreen"
    >
      <canvas
        v-show="running && frame"
        ref="canvas"
        class="live-canvas"
      />
      <div v-if="!running || waiting" class="live-placeholder">
        {{ running ? "等待直播画面..." : "点击「开始直播」启动实时视频流" }}
      </div>
      <div v-if="running && !isFullscreen" class="fullscreen-hint">
        ⛶ 点击画面全屏
      </div>
    </div>
  </section>
</template>

<style scoped>
.screen-frame {
  position: relative;
}

.screen-frame.can-fullscreen {
  cursor: pointer;
}

.live-canvas {
  width: 100%;
  height: auto;
  display: block;
}

/* 全屏状态下 canvas 铺满 */
.screen-frame.is-fullscreen {
  display: flex;
  align-items: center;
  justify-content: center;
  background: #000;
  touch-action: manipulation;
}

.screen-frame.is-fullscreen .live-canvas {
  width: auto;
  height: auto;
  max-width: 100vw;
  max-height: 100vh;
  object-fit: contain;
}

.screen-frame.is-fallback-fullscreen {
  position: fixed;
  left: var(--live-fallback-left, 0);
  top: var(--live-fallback-top, 0);
  z-index: 1000;
  width: var(--live-fallback-width, 100vw);
  height: var(--live-fallback-height, 100vh);
  min-height: 0;
  padding:
    env(safe-area-inset-top)
    env(safe-area-inset-right)
    env(safe-area-inset-bottom)
    env(safe-area-inset-left);
  border: 0;
  border-radius: 0;
  cursor: pointer;
  overscroll-behavior: none;
}

.screen-frame.is-fallback-fullscreen .live-canvas {
  max-width: calc(var(--live-fallback-width, 100vw) - env(safe-area-inset-left) - env(safe-area-inset-right));
  max-height: calc(var(--live-fallback-height, 100vh) - env(safe-area-inset-top) - env(safe-area-inset-bottom));
}

.live-placeholder {
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--text-dim, #888);
  font-size: 0.875rem;
  min-height: 200px;
}

.fullscreen-hint {
  position: absolute;
  bottom: 8px;
  right: 10px;
  background: rgba(0, 0, 0, 0.55);
  color: #ccc;
  font-size: 0.75rem;
  padding: 3px 8px;
  border-radius: 4px;
  pointer-events: none;
  transition: opacity 0.3s;
}

/* 全屏时隐藏提示 */
.screen-frame.is-fullscreen .fullscreen-hint {
  display: none;
}

.fps-label {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  font-size: 0.875rem;
  margin-left: 8px;
}

.fps-input {
  width: 48px;
  padding: 2px 4px;
  font-size: 0.875rem;
  border: 1px solid var(--border-color, #555);
  border-radius: 4px;
  background: var(--bg-input, #1a1a1a);
  color: var(--text-color, #eee);
  text-align: center;
}
</style>
