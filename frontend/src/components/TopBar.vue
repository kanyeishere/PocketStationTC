<script setup lang="ts">
import type { ConnectionMode } from "@/types";

defineProps<{
  connectionText: string;
  connectionMode: ConnectionMode;
}>();

const emit = defineEmits<{
  refresh: [];
}>();
</script>

<template>
  <header class="topbar">
    <div>
      <div class="title">Pocket Station</div>
      <div class="connection" :class="connectionMode">{{ connectionText }}</div>
    </div>
    <button class="icon-button" type="button" title="刷新" @click="emit('refresh')">↻</button>
  </header>
</template>
