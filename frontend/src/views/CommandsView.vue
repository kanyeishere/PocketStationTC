<script setup lang="ts">
import { computed } from "vue";
import type { HealthInfo } from "@/types";

const props = defineProps<{
  serverInfo: HealthInfo | null;
}>();

const emit = defineEmits<{
  command: [value: string];
}>();

const commands = [
  { label: "Echo", value: "/e Pocket Station online" },
  { label: "小队频道", value: "/p " },
  { label: "部队频道", value: "/fc " },
  { label: "回复私聊", value: "/r " }
];

const serverInfoText = computed(() => JSON.stringify(props.serverInfo || {}, null, 2));
</script>

<template>
  <section id="tab-commands" class="view">
    <div class="section-title">快捷指令</div>
    <div class="command-grid">
      <button
        v-for="command in commands"
        :key="command.label"
        type="button"
        @click="emit('command', command.value)"
      >
        {{ command.label }}
      </button>
    </div>

    <div class="section-title">服务器</div>
    <pre class="panel pre">{{ serverInfoText }}</pre>
  </section>
</template>
