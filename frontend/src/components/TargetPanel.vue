<script setup lang="ts">
import CharacterPanel from "@/components/CharacterPanel.vue";
import type { CharacterState } from "@/types";

defineProps<{
  character: CharacterState | null | undefined;
}>();
</script>

<template>
  <div class="section-title">目标</div>
  <CharacterPanel :character="character" empty-text="无目标" />
</template>
