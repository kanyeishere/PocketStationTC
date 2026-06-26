<script setup lang="ts">
import { ref, watch } from "vue";

const props = defineProps<{
  isActive: boolean;
  fps: number;
  running: boolean;
  frame: ImageBitmap | null;
  frameSize: string;
}>();

const emit = defineEmits<{
  start: [fps: number];
  stop: [];
}>();

const canvas = ref<HTMLCanvasElement | null>(null);
const fpsInput = ref(props.fps);
let animating = false;

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

watch(() => props.frame, (bitmap) => {
  if (!bitmap || !canvas.value) return;
  const c = canvas.value;
  const ctx = c.getContext("2d");
  if (!ctx) return;

  // Resize canvas to match frame on first frame or dimension change
  if (c.width !== bitmap.width || c.height !== bitmap.height) {
    c.width = bitmap.width;
    c.height = bitmap.height;
  }

  if (!animating) {
    animating = true;
    requestAnimationFrame(() => {
      ctx.drawImage(bitmap, 0, 0);
      animating = false;
    });
  }
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

watch(() => props.isActive, (active, _prev) => {
  if (active && !props.running) {
    // Auto-start stream when entering the tab
    emit("start", fpsInput.value);
  } else if (!active && props.running) {
    // Auto-stop stream when leaving the tab
    emit("stop");
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
      <span>{{ running ? `● ${frameSize}` : "○ 未推流" }}</span>
    </div>
    <div class="screen-frame">
      <canvas
        v-show="running"
        ref="canvas"
        class="live-canvas"
      />
      <div v-if="!running" class="live-placeholder">
        点击「开始直播」启动实时视频流
      </div>
    </div>
  </section>
</template>

<style scoped>
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

.live-canvas {
  width: 100%;
  height: auto;
  display: block;
}

.live-placeholder {
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--text-dim, #888);
  font-size: 0.875rem;
  min-height: 200px;
}
</style>
