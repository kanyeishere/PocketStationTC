<script setup lang="ts">
defineProps<{
  imageUrl: string;
  loading: boolean;
  meta: string;
}>();

const emit = defineEmits<{
  capture: [];
}>();
</script>

<template>
  <section id="tab-screen" class="view">
    <div class="screen-toolbar">
      <button type="button" :disabled="loading" @click="emit('capture')">请求截图</button>
      <span>{{ meta }}</span>
    </div>
    <div class="screen-frame">
      <img v-if="imageUrl" :src="imageUrl" alt="游戏截图">
    </div>
  </section>
</template>
